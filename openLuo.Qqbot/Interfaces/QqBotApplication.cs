using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.OneBot;
using static openLuo.Infrastructure.Logging.Logger;

namespace openLuo.Interfaces.QQbot;

/// <summary>
/// QQ bot 平台业务宿主(OneBot 11 通道)。
/// 职责:把 OneBot 消息事件翻译为 openLuo 会话语义(Observe/命令/回合),把回合产物渲染为
/// OneBot 段发出。协议(WS/事件/段)在 openLuo.OneBot 层,本类只做 QQ 场景业务。
/// </summary>
public sealed class QqBotApplication
{
    private readonly IAgentRuntime _runtime;
    private readonly IQqBotConfigCenter _configCenter;
    private readonly IOutputQueue _outputQueue;
    private readonly IGameLogger? _logger;
    private const int LogTextMax = 300;

    private long _selfId;

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
            Console.Error.WriteLine("QQbot config missing baseAddress (OneBot 正向 WebSocket 地址,如 ws://localhost:3001/).");
            return;
        }
        if (config.TargetGroupIds.Count == 0 && config.TargetFriendIds.Count == 0)
        {
            Console.Error.WriteLine("QQbot config has no valid targets. Set targetGroupIds or targetFriendIds.");
            return;
        }

        // 图片下载 client:localhost 协议端与网易云 CDN 都禁代理(代理会劫持本地与图片请求)。
        var handler = new SocketsHttpHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds)) };

        var client = new OneBotWebSocketClient(
            new Uri(config.BaseAddress),
            accessToken: config.AccessToken,
            logger: _logger is null ? null : new OneBotLoggerAdapter(_logger));

        // 连接就绪 → 获取自身 QQ 号(过滤自己消息 / @ 识别 / get_login_info)
        client.ConnectionStateChanged += state =>
        {
            if (state == "connected" && Interlocked.Read(ref _selfId) == 0)
                _ = RefreshSelfIdAsync(client, ct);
        };
        client.MessageReceived += ev =>
        {
            // fire-and-forget 事件处理;每事件独立回合,异常隔离
            _ = HandleMessageAsync(client, http, ev, ct);
        };

        // 中途消息即时推送(D50 队列消费):回合内 inqueue 消息实时发送到对应频道。
        if (config.SendInterimMessages)
            _ = ConsumeInterimQueueAsync(client, ct);

        // 阻塞至取消:内部含断线指数退避重连(1s→30s)
        await client.RunAsync(ct);
    }

    private async Task RefreshSelfIdAsync(OneBotWebSocketClient client, CancellationToken ct)
    {
        var response = await client.CallApiAsync("get_login_info", [], ct);
        try
        {
            var userId = response?["data"]?["user_id"]?.GetValue<long>() ?? 0;
            if (userId != 0)
                Interlocked.Exchange(ref _selfId, userId);
        }
        catch (Exception ex)
        {
            _logger?.Warn("qq", $"get_login_info parse failed: {ex.Message}");
        }
    }

    /// <summary>OneBot 消息事件 → QQ 场景处理(目标过滤/自消息/@/Observe/命令/回合)。</summary>
    private async Task HandleMessageAsync(OneBotWebSocketClient client, HttpClient http, OneBotMessageEvent ev, CancellationToken ct)
    {
        try
        {
            var current = _configCenter.GetSnapshot();
            var selfId = Interlocked.Read(ref _selfId) != 0 ? _selfId : ev.SelfId;   // 事件自带 self_id 兜底
            var isGroup = ev.MessageType == "group";

            if (isGroup)
            {
                if (!ev.GroupId.HasValue || !current.TargetGroupIds.Contains(ev.GroupId.Value) || ev.UserId == selfId)
                    return;
                var text = ev.ToPlainText(selfId);
                if (string.IsNullOrWhiteSpace(text))
                    return;
                var imageBlocks = await DownloadImagesAsync(http, ev.ImageUrls(), ct);
                var senderName = ev.SenderDisplayName ?? string.Empty;
                LogMessage(current, $"[recv] group={ev.GroupId} from={senderName}({ev.UserId}): {Truncate(text)}");

                if (current.ReplyOnlyWhenMentioned && !ev.Mentions(selfId))
                {
                    // 感知通道:未 @ 消息写入会话历史("看但不回"),不触发 LLM 回合
                    await new QqRuntimeBridge(_runtime, current).ObserveAsync("group", ev.GroupId.Value, text, senderName, imageBlocks, ct);
                    return;
                }
                if (text.StartsWith('/'))
                {
                    var reply = await TryRunCommandAsync(text, current, ev.UserId, ev.GroupId.Value, isGroup: true, ct);
                    if (reply is not null)
                        await SendTextAsync(client, isGroup: true, ev.GroupId.Value, reply, current, $"group={ev.GroupId}", ct);
                    return;
                }
                var result = await new QqRuntimeBridge(_runtime, current).HandleAsync("group", ev.GroupId.Value, ev.UserId, text, senderName, imageBlocks, ct);
                await SendPartsAsync(client, isGroup: true, ev.GroupId.Value, QqRuntimeBridge.Render(result), current, $"group={ev.GroupId}", ct);
            }
            else if (ev.MessageType == "private")
            {
                if (!current.TargetFriendIds.Contains(ev.UserId) || ev.UserId == selfId)
                    return;
                var text = ev.ToPlainText(null);
                if (string.IsNullOrWhiteSpace(text))
                    return;
                var imageBlocks = await DownloadImagesAsync(http, ev.ImageUrls(), ct);
                var senderName = ev.SenderDisplayName ?? string.Empty;
                LogMessage(current, $"[recv] friend={ev.UserId} from={senderName}: {Truncate(text)}");

                if (text.StartsWith('/'))
                {
                    var reply = await TryRunCommandAsync(text, current, ev.UserId, ev.UserId, isGroup: false, ct);
                    if (reply is not null)
                        await SendTextAsync(client, isGroup: false, ev.UserId, reply, current, $"friend={ev.UserId}", ct);
                    return;
                }
                var result = await new QqRuntimeBridge(_runtime, current).HandleAsync("friend", ev.UserId, ev.UserId, text, senderName, imageBlocks, ct);
                await SendPartsAsync(client, isGroup: false, ev.UserId, QqRuntimeBridge.Render(result), current, $"friend={ev.UserId}", ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("qq", $"message handler failed: {ex.Message}");
        }
    }

    private async Task SendTextAsync(OneBotWebSocketClient client, bool isGroup, long targetId, string text, QqBotConfig config, string logLabel, CancellationToken ct)
    {
        LogMessage(config, $"[send] {logLabel}: {Truncate(text)}");
        var result = isGroup
            ? await client.SendGroupMessageAsync(targetId, [OneBotSegment.Text(text)], ct)
            : await client.SendPrivateMessageAsync(targetId, [OneBotSegment.Text(text)], ct);
        if (!result.Ok)
            _logger?.Error("qq", $"text send failed: {result.Error}");
    }

    /// <summary>中途消息即时推送(D50 队列消费)。按 ConversationId 路由到对应频道,发送后 Ack。
    /// 非 QQ 会话条目直接 Ack(防队列堆积阻塞);非永久失败保留可重试。</summary>
    private async Task ConsumeInterimQueueAsync(OneBotWebSocketClient client, CancellationToken ct)
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
                        await SendPartsAsync(client, isGroup: true, groupId, [ToReplyPart(item)], snapshot, $"group={groupId}", ct);
                    }
                    else if (conversationId.StartsWith("qq-friend-", StringComparison.Ordinal)
                             && long.TryParse(conversationId.AsSpan("qq-friend-".Length), out var friendId))
                    {
                        var snapshot = _configCenter.GetSnapshot();
                        await SendPartsAsync(client, isGroup: false, friendId, [ToReplyPart(item)], snapshot, $"friend={friendId}", ct);
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

    /// <summary>渲染 parts → 按序发送若干条 OneBot 消息(text/image 合并一条,每个音乐卡独占一条)。</summary>
    /// <remarks>
    /// LLBot/QQ 对 text+music 同消息混发不稳(实测只出 text),故音乐卡必须单独成条;
    /// 超过 <see cref="MaxMusicCardsPerTurn"/> 张时其余候选折叠为文本链接。
    /// </remarks>
    private async Task SendPartsAsync(
        OneBotWebSocketClient client, bool isGroup, long targetId,
        IReadOnlyList<QqReplyPart> parts, QqBotConfig config, string logLabel, CancellationToken ct)
    {
        var segments = new List<OneBotSegment>();
        foreach (var part in parts)
        {
            switch (part.Kind)
            {
                case "image" when ExtractBase64(part.Value) is { } image:
                    segments.Add(OneBotSegment.Image($"base64://{image}"));
                    break;
                case "music" when long.TryParse(part.Value, out var songId):
                    segments.Add(OneBotSegment.Music163(songId));
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(part.Value))
                        segments.Add(OneBotSegment.Text(part.Value));
                    break;
            }
        }
        if (segments.Count == 0)
            return;

        foreach (var batch in SplitMusicBatches(segments))
        {
            var result = isGroup
                ? await client.SendGroupMessageAsync(targetId, batch, ct)
                : await client.SendPrivateMessageAsync(targetId, batch, ct);
            LogMessage(config, $"[send] {logLabel}: {Truncate(string.Join(" | ", batch.Select(SegmentToLog)))}");
            if (!result.Ok)
                _logger?.Error("qq", $"message send failed: {result.Error}");
        }
    }

    /// <summary>单回合最多发出的音乐卡数;超出部分折叠为文本链接。</summary>
    internal const int MaxMusicCardsPerTurn = 3;

    /// <summary>把段列表拆成若干条可发送的消息。</summary>
    /// <remarks>
    /// LLBot/QQ 对 text+music 同消息混发不稳(实测只出 text),故音乐卡必须独占一条消息:
    /// 非音乐段(text/image)按出场顺序合并为一条(多条连续文本/图片归一条),
    /// 每个独占的音乐卡单独成一条,保持整体出场顺序(卡仍夹在相邻文本之间)。
    /// 超出 <see cref="MaxMusicCardsPerTurn"/> 的音乐卡折叠为文本链接附在最后一条。
    /// </remarks>
    internal static IReadOnlyList<List<OneBotSegment>> SplitMusicBatches(List<OneBotSegment> segments)
    {
        var batches = new List<List<OneBotSegment>>();
        var text = new List<OneBotSegment>();
        var cards = new List<OneBotSegment>();
        var overflow = new List<OneBotSegment>();

        void FlushText()
        {
            if (text.Count > 0)
            {
                batches.Add(text);
                text = [];
            }
        }

        foreach (var seg in segments)
        {
            if (seg.Type != "music")
            {
                text.Add(seg);
                continue;
            }
            FlushText();
            if (cards.Count >= MaxMusicCardsPerTurn)
            {
                overflow.Add(seg);
                continue;
            }
            cards.Add(seg);
            batches.Add([seg]);   // 每张卡独占一条消息
        }
        FlushText();

        if (overflow.Count > 0)
        {
            var note = "还有 " + overflow.Count + " 首候选(超单次发卡上限):\n"
                + string.Join("\n", overflow.Select(m => $"https://music.163.com/#/song?id={m.Data["id"]}"));
            batches.Add([OneBotSegment.Text(note)]);
        }
        return batches;
    }

    /// <summary>段 → 日志文本(文本原样,music 显示资源 id,其余显示类型)。</summary>
    private static string SegmentToLog(OneBotSegment seg) => seg.Type switch
    {
        "text" => seg.Data["text"]?.GetValue<string>() ?? string.Empty,
        "music" => $"[music:{seg.Data["id"]}]",
        _ => $"[{seg.Type}]"
    };

    /// <summary>平台命令分发:仅 admin 可执行;返回要发送的文本,null 表示已处理或不应回复。</summary>
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

    /// <summary>下载图片并转 base64 data URI(供视觉模型消费)。失败/超时/超大图跳过,不阻塞消息处理。</summary>
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
            // base64 后约 +33%,OpenAI 兼容接口 image 载荷上限 4MB——超过 3MB 跳过
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

    private static string? ExtractBase64(string value)
    {
        var index = value.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : value[(index + 7)..];
    }

    private static string Truncate(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? "(empty)"
            : text.Length <= LogTextMax ? text : text[..LogTextMax] + $"…(+{text.Length - LogTextMax}ch)";

    /// <summary>QQ bot 消息收发日志(interface 层配置 qqbot.jsonc logMessages 控制,热加载)。</summary>
    private void LogMessage(QqBotConfig config, string message)
    {
        if (config.LogMessages)
            _logger?.Info("qq", message);
    }
}

/// <summary>IGameLogger → Microsoft.Extensions.Logging.ILogger 适配(OneBot 层零 openLuo 依赖)。</summary>
internal sealed class OneBotLoggerAdapter : Microsoft.Extensions.Logging.ILogger
{
    private readonly IGameLogger _inner;
    public OneBotLoggerAdapter(IGameLogger inner) => _inner = inner;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        switch (logLevel)
        {
            case Microsoft.Extensions.Logging.LogLevel.Error:
                _inner.Error("qq", message);
                break;
            case Microsoft.Extensions.Logging.LogLevel.Warning:
                _inner.Warn("qq", message);
                break;
            case Microsoft.Extensions.Logging.LogLevel.Debug:
            case Microsoft.Extensions.Logging.LogLevel.Trace:
                _inner.Debug("qq", message);
                break;
            default:
                _inner.Info("qq", message);
                break;
        }
    }
}
