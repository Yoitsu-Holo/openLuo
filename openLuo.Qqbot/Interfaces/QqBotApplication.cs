using Milky.Net.Client;
using Milky.Net.Model;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using static openLuo.Infrastructure.Logging.Logger;

namespace openLuo.Interfaces.QQbot;

public sealed class QqBotApplication
{
    private readonly IAgentRuntime _runtime;
    private readonly IQqBotConfigCenter _configCenter;
    private readonly IOutputQueue _outputQueue;
    private readonly IGameLogger? _logger;
    private const int LogTextMax = 300;

    public QqBotApplication(IAgentRuntime runtime, IQqBotConfigCenter configCenter, IOutputQueue outputQueue, IGameLogger? logger = null)
    {
        _runtime = runtime;
        _configCenter = configCenter;
        _outputQueue = outputQueue;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var config = _configCenter.GetSnapshot();
        if (!config.Enabled)
        {
            Console.Error.WriteLine("QQbot is disabled. Set enabled=true in qqbot.jsonc.");
            return;
        }
        if (string.IsNullOrWhiteSpace(config.BaseAddress))
        {
            Console.Error.WriteLine("QQbot config missing baseAddress.");
            return;
        }
        if (config.TargetGroupIds.Count == 0 && config.TargetFriendIds.Count == 0)
        {
            Console.Error.WriteLine("QQbot config has no valid targets. Set targetGroupIds or targetFriendIds.");
            return;
        }
        // localhost 内部服务请求禁代理：http_proxy/https_proxy 等环境变量会把对本地
        // 协议端的请求劫持到外部代理，表现为连接被提前切断（ResponseEnded）/代理 503。
        var handler = new SocketsHttpHandler { UseProxy = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(config.BaseAddress), Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds)) };
        var milky = new MilkyClient(http);
        var login = await ConnectWithRetryAsync(milky, ct);
        if (login is null)
            return; // 握手失败：错误已输出，优雅退出（不 core dump）
        // 中途消息即时推送（D50 队列消费）：回合内 inqueue 消息实时发送到对应频道。
        // 最终回复仍走 TurnResult 同步路径（回合结束后 Render），顺序天然正确（中途在前）。
        if (config.SendInterimMessages)
            _ = ConsumeInterimQueueAsync(milky, http, ct);
        milky.Events.MessageReceive += async (_, args) =>
        {
            try
            {
                var current = _configCenter.GetSnapshot();
                if (args.Data is GroupIncomingMessage group)
                {
                    if (!current.TargetGroupIds.Contains(group.Group.GroupId) || group.GroupMember.UserId == login.Uin) return;
                    var text = ExtractText(group.Segments, login.Uin);
                    if (string.IsNullOrWhiteSpace(text)) return;
                    var imageBlocks = await DownloadImagesAsync(http, CollectImageUrls(group.Segments), ct);
                    var member = group.GroupMember;
                    var senderName = string.IsNullOrWhiteSpace(member?.Card) ? member?.Nickname : member?.Card;
                    LogMessage(current,
                        $"[recv] group={group.Group.GroupId} from={senderName}({group.GroupMember.UserId}): {Truncate(text)}");
                    var mentioned = group.Segments.OfType<IncomingSegment<MentionIncomingSegmentData>>().Any(s => s.Data.UserId == login.Uin);
                    if (current.ReplyOnlyWhenMentioned && !mentioned)
                    {
                        // 感知通道：未 @ 消息写入会话历史（"看但不回"），不触发 LLM 回合
                        await new QqRuntimeBridge(_runtime, current).ObserveAsync("group", group.Group.GroupId, text, senderName, imageBlocks, ct);
                        return;
                    }
                    if (text.StartsWith('/'))
                    {
                        var reply = await TryRunCommandAsync(text, current, group.GroupMember.UserId, group.Group.GroupId, isGroup: true, ct);
                        if (reply is not null)
                        {
                            LogMessage(current, $"[send] group={group.Group.GroupId}: {Truncate(reply)}");
                            await milky.Message.SendGroupMessageAsync(new SendGroupMessageRequest(group.Group.GroupId, [new OutgoingSegment<TextOutgoingSegmentData>(new TextOutgoingSegmentData(reply))]), ct);
                        }
                        return;
                    }
                    var result = await new QqRuntimeBridge(_runtime, current).HandleAsync("group", group.Group.GroupId, group.GroupMember.UserId, text, senderName, imageBlocks, ct);
                    var rendered = QqRuntimeBridge.Render(result);
                    await SendPartsAsync(milky, http, isGroup: true, group.Group.GroupId, rendered, current, $"group={group.Group.GroupId}", ct);
                }
                else if (args.Data is FriendIncomingMessage friend)
                {
                    if (!current.TargetFriendIds.Contains(friend.Friend.UserId) || friend.SenderId == login.Uin) return;
                    var text = ExtractText(friend.Segments, null);
                    if (string.IsNullOrWhiteSpace(text)) return;
                    var imageBlocks = await DownloadImagesAsync(http, CollectImageUrls(friend.Segments), ct);
                    var friendName = string.IsNullOrWhiteSpace(friend.Friend.Nickname) ? friend.Friend.Remark : friend.Friend.Nickname;
                    LogMessage(current,
                        $"[recv] friend={friend.Friend.UserId} from={friendName}: {Truncate(text)}");
                    if (text.StartsWith('/'))
                    {
                        var reply = await TryRunCommandAsync(text, current, friend.Friend.UserId, friend.Friend.UserId, isGroup: false, ct);
                        if (reply is not null)
                        {
                            LogMessage(current, $"[send] friend={friend.Friend.UserId}: {Truncate(reply)}");
                            await milky.Message.SendPrivateMessageAsync(new SendPrivateMessageRequest(friend.Friend.UserId,
                                [new OutgoingSegment<TextOutgoingSegmentData>(new TextOutgoingSegmentData(reply))]), ct);
                        }
                        return;
                    }
                    var result = await new QqRuntimeBridge(_runtime, current).HandleAsync("friend", friend.Friend.UserId, friend.SenderId, text, friendName, imageBlocks, ct);
                    var rendered = QqRuntimeBridge.Render(result);
                    await SendPartsAsync(milky, http, isGroup: false, friend.Friend.UserId, rendered, current, $"friend={friend.Friend.UserId}", ct);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("qq", $"message handler failed: {ex.Message}");
            }
        };
        // WebSocket 接收循环：远端断连（服务器重启/网络波动/代理断开）时自动重连，不退出进程。
        await ReceiveLoopAsync(milky, ct);
    }

    /// <summary>中途消息即时推送（D50 队列消费）。按 ConversationId 路由到对应频道，发送后 Ack。
    /// 非 QQ 会话条目直接 Ack（防队列堆积阻塞）；非永久失败保留可重试。</summary>
    private async Task ConsumeInterimQueueAsync(MilkyClient milky, HttpClient http, CancellationToken ct)
    {
        try
        {
            await foreach (var item in _outputQueue.ReadAsync(ct))
            {
                var conversationId = item.ConversationId ?? string.Empty;
                try
                {
                    if (conversationId.StartsWith("qq-group-", StringComparison.Ordinal)
                        && long.TryParse(conversationId.AsSpan("qq-group-".Length), out var groupId))
                    {
                        var snapshot = _configCenter.GetSnapshot();
                        await SendPartsAsync(milky, http, isGroup: true, groupId, [ToReplyPart(item)], snapshot, $"group={groupId}", ct);
                    }
                    else if (conversationId.StartsWith("qq-friend-", StringComparison.Ordinal)
                             && long.TryParse(conversationId.AsSpan("qq-friend-".Length), out var friendId))
                    {
                        var snapshot = _configCenter.GetSnapshot();
                        await SendPartsAsync(milky, http, isGroup: false, friendId, [ToReplyPart(item)], snapshot, $"friend={friendId}", ct);
                    }
                    await _outputQueue.AckAsync(item.Sequence, ct);
                }
                catch (Exception ex)
                {
                    _logger?.Error("qq", $"interim message send failed: {ex.Message}");
                    await _outputQueue.FailAsync(item.Sequence, permanent: false, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
    }

    private static QqReplyPart ToReplyPart(OutputItem item) => item.Kind switch
    {
        ReplyItemKind.Image => new("image", Convert.ToString(item.Payload) ?? string.Empty),
        ReplyItemKind.Card => QqRuntimeBridge.TryRenderCard(item) ?? new("text", Convert.ToString(item.Payload) ?? string.Empty),
        _ => new("text", Convert.ToString(item.Payload) ?? string.Empty)
    };

    /// <summary>平台命令分发：仅 admin 可执行；返回要发送的文本，null 表示已处理或不应回复。</summary>
    private async Task<string?> TryRunCommandAsync(string text, QqBotConfig config, long actorId, long channelId, bool isGroup, CancellationToken ct)
    {
        if (!config.AdminUsers.Contains(actorId))
            return "Permission Denied";

        var command = text.TrimStart('/').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        return command switch
        {
            "context" => await _runtime.GetContextSummaryAsync($"qq-{(isGroup ? "group" : "friend")}-{channelId}", ct)
                ?? "session not open",
            "help" => "commands: /context /help",
            _ => $"unknown command: /{command}"
        };
    }

    public static string ExtractText(IEnumerable<IncomingSegment> segments, long? botUserId)
    {
        var parts = new List<string>();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case IncomingSegment<TextIncomingSegmentData> text: parts.Add(text.Data.Text); break;
                case IncomingSegment<MentionIncomingSegmentData> mention when mention.Data.UserId != botUserId: parts.Add($"@{mention.Data.Name}"); break;
                case IncomingSegment<ImageIncomingSegmentData>: parts.Add("[image]"); break;
                case IncomingSegment<RecordIncomingSegmentData>: parts.Add("[voice]"); break;
                case IncomingSegment<FileIncomingSegmentData>: parts.Add("[file]"); break;
            }
        }
        return string.Join(' ', parts).Trim();
    }

    /// <summary>收集消息中的图片下载地址（TempUrl）。文本侧保留 [image] 占位，图片本体随 blocks 走多模态通道。</summary>
    public static IReadOnlyList<string> CollectImageUrls(IEnumerable<IncomingSegment> segments)
    {
        var urls = segments.OfType<IncomingSegment<ImageIncomingSegmentData>>()
            .Select(s => s.Data.TempUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();
        var total = segments.OfType<IncomingSegment<ImageIncomingSegmentData>>().Count();
        if (total > urls.Count)
            Info("qq", $"image segment(s) without TempUrl: {total - urls.Count}/{total} skipped (image not attached)");
        return urls;
    }

    /// <summary>下载图片并转 base64 data URI（供视觉模型消费）。失败/超时/超大图跳过，不阻塞消息处理。</summary>
    internal static async Task<IReadOnlyList<ImageBlock>> DownloadImagesAsync(HttpClient http, IEnumerable<string> urls, CancellationToken ct)
    {
        var blocks = await Task.WhenAll(urls.Select(url => DownloadImageAsync(http, url, ct)));
        return blocks.Where(b => b is not null).Cast<ImageBlock>().ToList();
    }

    private static async Task<ImageBlock?> DownloadImageAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var mime = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mime) || !mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                mime = "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            // base64 后约 +33%，OpenAI 兼容接口 image 载荷上限 4MB——超过 3MB 跳过
            const int maxBytes = 3 * 1024 * 1024;
            if (bytes.Length > maxBytes)
            {
                Info("qq", $"image download skipped (too large: {bytes.Length} bytes): {Truncate(url)}");
                return null;
            }
            return new ImageBlock
            {
                Kind = BlockKind.Image,
                AssetId = url,
                MimeType = mime,
                Name = url,
                DataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}"
            };
        }
        catch (Exception ex)
        {
            Info("qq", $"image download failed: {ex.Message} ({Truncate(url)})");
            return null;
        }
    }

    private static List<OutgoingSegment> ToSegments(IReadOnlyList<QqReplyPart> parts)
    {
        var result = new List<OutgoingSegment>();
        foreach (var part in parts)
        {
            if (part.Kind == "image" && ExtractBase64(part.Value) is { } image)
                result.Add(new OutgoingSegment<ImageOutgoingSegmentData>(new ImageOutgoingSegmentData(new MilkyUri($"base64://{image}"), "image", SubType.Normal)));
            else if (!string.IsNullOrWhiteSpace(part.Value))
                result.Add(new OutgoingSegment<TextOutgoingSegmentData>(new TextOutgoingSegmentData(part.Value)));
        }
        return result;
    }

    private static string? ExtractBase64(string value)
    {
        var index = value.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : value[(index + 7)..];
    }

    private static string Truncate(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? "(empty)"
            : text.Length <= LogTextMax ? text : text[..LogTextMax] + $"…(+{text.Length - LogTextMax}ch)";

    /// <summary>QQ bot 消息收发日志（interface 层配置 qqbot.jsonc logMessages 控制，热加载）。</summary>
    private void LogMessage(QqBotConfig config, string message)
    {
        if (config.LogMessages)
            _logger?.Info("qq", message);
    }

    /// <summary>渲染结果发送：music 卡经报文直发（milky IR 无 music 段），
    /// 其余 text/image 走 milky SDK 段；普通段为空且无 music 时静默（无日志无发送）。</summary>
    private async Task SendPartsAsync(
        MilkyClient milky, HttpClient http, bool isGroup, long targetId,
        IReadOnlyList<QqReplyPart> rendered, QqBotConfig config, string logLabel, CancellationToken ct)
    {
        var music = rendered.Where(p => p.Kind == "music").ToList();
        var regular = rendered.Where(p => p.Kind != "music").ToList();
        var segments = ToSegments(regular);
        if (segments.Count > 0)
        {
            LogMessage(config, $"[send] {logLabel}: {Truncate(string.Join(" | ", regular.Select(p => p.Kind == "text" ? p.Value : $"[{p.Kind}]")))}");
            if (isGroup)
                await milky.Message.SendGroupMessageAsync(new SendGroupMessageRequest(targetId, [.. segments]), ct);
            else
                await milky.Message.SendPrivateMessageAsync(new SendPrivateMessageRequest(targetId, [.. segments]), ct);
        }
        foreach (var part in music)
        {
            LogMessage(config, $"[send] {logLabel}: [music:{part.Value}]");
            await SendMusicCardAsync(http, isGroup, targetId, part.Value, ct);
        }
    }

    /// <summary>网易云音乐卡直发：milky 协议段类型不含 music（IR 10 种段），
    /// 故按协议端报文直发 send_group_message / send_private_message，body 为
    /// OneBot 兼容 music 段（type "163"），LLBot 据此渲染原生网易云卡片。</summary>
    private async Task SendMusicCardAsync(HttpClient http, bool isGroup, long targetId, string musicId, CancellationToken ct)
    {
        if (!long.TryParse(musicId, out _))
        {
            _logger?.Error("qq", $"music card skipped: non-numeric id '{Truncate(musicId)}'");
            return;
        }
        var api = isGroup ? "send_group_message" : "send_private_message";
        var targetKey = isGroup ? "group_id" : "user_id";
        var body = $"{{\"{targetKey}\":{targetId},\"message\":[{{\"type\":\"music\",\"data\":{{\"type\":\"163\",\"id\":{musicId}}}}}]}}";
        try
        {
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"/api/{api}", content, ct);
            var respBody = await response.Content.ReadAsStringAsync(ct);
            // milky 业务错误是 HTTP 200 + {"status":"failed","retcode":-400,...}：
            // 只看 HTTP 状态会静默吞错（music 段被协议端拒绝即此形态），必须解析业务码。
            if (!response.IsSuccessStatusCode)
            {
                _logger?.Error("qq", $"music card send failed ({(int)response.StatusCode}): {Truncate(respBody)}");
                return;
            }
            if (MilkyResponseIsError(respBody, out var errText))
                _logger?.Error("qq", $"music card send failed: {errText}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Error("qq", $"music card send failed: {ex.Message}");
        }
    }

    /// <summary>登录握手重试：协议端（LLBot）可能尚未就绪或握手瞬断。
    /// 指数退避 1s→10s 封顶、最多 5 次；全部失败返回 null（调用方已输出错误并优雅退出）。</summary>
    private static async Task<Milky.Net.Model.GetLoginInfoResponse?> ConnectWithRetryAsync(MilkyClient milky, CancellationToken ct)
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await milky.System.GetLoginInfoAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[qq] login handshake failed (attempt {attempt}/{maxAttempts}): {ex.Message}");
                if (attempt == maxAttempts)
                    return null;
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
            }
        }
        return null;
    }

    /// <summary>milky 响应业务码解析：{"status":"failed","retcode":N,...} 或非零 retcode 视为错误。</summary>
    private static bool MilkyResponseIsError(string respBody, out string errorText)
    {
        errorText = string.Empty;
        if (string.IsNullOrWhiteSpace(respBody))
            return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(respBody);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return false;
            if (root.TryGetProperty("status", out var status)
                && status.ValueKind == System.Text.Json.JsonValueKind.String
                && status.GetString() == "failed")
            {
                errorText = Truncate(respBody);
                return true;
            }
            if (root.TryGetProperty("retcode", out var retcode)
                && retcode.ValueKind == System.Text.Json.JsonValueKind.Number
                && retcode.TryGetInt32(out var code)
                && code != 0)
            {
                errorText = Truncate(respBody);
                return true;
            }
            return false;
        }
        catch
        {
            return false;   // 非 JSON 响应按成功处理（无业务语义可解析）
        }
    }

    /// <summary>WebSocket 接收重连循环：远端断连（服务器重启/网络波动/代理断开）时
    /// 自动重连，不退出进程。事件处理器在 milky.Events 上注册一次，重连仅恢复接收通道。
    /// 连接曾成功建立则重置退避（快速恢复）；连续失败指数退避 1s→30s 封顶；ct 取消立即退出。</summary>
    private async Task ReceiveLoopAsync(MilkyClient milky, CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        const double MaxRetrySeconds = 30;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await milky.ReceivingEventUsingWebSocketAsync(
                    static ws => ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30), ct);
                retryDelay = TimeSpan.FromSeconds(1); // 连接曾成功建立，重置退避
                if (ct.IsCancellationRequested) return;
                _logger?.Warn("qq", "WebSocket receive ended without exception; reconnecting.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // 正常关闭
            }
            catch (Exception ex)
            {
                _logger?.Error("qq", $"WebSocket receive failed: {ex.Message}; reconnect in {retryDelay.TotalSeconds:0}s");
            }
            try
            {
                await Task.Delay(retryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaxRetrySeconds));
        }
    }
}
