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
        using var http = new HttpClient { BaseAddress = new Uri(config.BaseAddress), Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds)) };
        var milky = new MilkyClient(http);
        var login = await milky.System.GetLoginInfoAsync(ct);
        // 中途消息即时推送（D50 队列消费）：回合内 inqueue 消息实时发送到对应频道。
        // 最终回复仍走 TurnResult 同步路径（回合结束后 Render），顺序天然正确（中途在前）。
        if (config.SendInterimMessages)
            _ = ConsumeInterimQueueAsync(milky, ct);
        milky.Events.MessageReceive += async (_, args) =>
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
                var segments = ToSegments(rendered);
                if (segments.Count > 0)
                {
                    LogMessage(current, $"[send] group={group.Group.GroupId}: {Truncate(string.Join(" | ", rendered.Select(p => p.Kind == "text" ? p.Value : $"[{p.Kind}]")))}");
                    await milky.Message.SendGroupMessageAsync(new SendGroupMessageRequest(group.Group.GroupId, [.. segments]), ct);
                }
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
                var segments = ToSegments(rendered);
                if (segments.Count > 0)
                {
                    LogMessage(current, $"[send] friend={friend.Friend.UserId}: {Truncate(string.Join(" | ", rendered.Select(p => p.Kind == "text" ? p.Value : $"[{p.Kind}]")))}");
                    await milky.Message.SendPrivateMessageAsync(new SendPrivateMessageRequest(friend.Friend.UserId, [.. segments]), ct);
                }
            }
        };
        await milky.ReceivingEventUsingWebSocketAsync(static ws => ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30), ct);
    }

    /// <summary>中途消息即时推送（D50 队列消费）。按 ConversationId 路由到对应频道，发送后 Ack。
    /// 非 QQ 会话条目直接 Ack（防队列堆积阻塞）；非永久失败保留可重试。</summary>
    private async Task ConsumeInterimQueueAsync(MilkyClient milky, CancellationToken ct)
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
                        var segments = ToSegments([ToReplyPart(item)]);
                        if (segments.Count > 0)
                            await milky.Message.SendGroupMessageAsync(new SendGroupMessageRequest(groupId, [.. segments]), ct);
                    }
                    else if (conversationId.StartsWith("qq-friend-", StringComparison.Ordinal)
                             && long.TryParse(conversationId.AsSpan("qq-friend-".Length), out var friendId))
                    {
                        var segments = ToSegments([ToReplyPart(item)]);
                        if (segments.Count > 0)
                            await milky.Message.SendPrivateMessageAsync(new SendPrivateMessageRequest(friendId, [.. segments]), ct);
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
}
