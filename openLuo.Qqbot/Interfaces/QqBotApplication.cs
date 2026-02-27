using Milky.Net.Client;
using Milky.Net.Model;
using openLuo.Capabilities.Core;
using openLuo.Core.Interfaces;

namespace openLuo.Interfaces.QQbot;

public sealed class QqBotApplication
{
    private readonly IAgentRuntime _runtime;
    private readonly IQqBotConfigCenter _configCenter;
    private readonly IGameLogger? _logger;
    private const int LogTextMax = 300;

    public QqBotApplication(IAgentRuntime runtime, IQqBotConfigCenter configCenter, IGameLogger? logger = null)
    {
        _runtime = runtime;
        _configCenter = configCenter;
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
        milky.Events.MessageReceive += async (_, args) =>
        {
            var current = _configCenter.GetSnapshot();
            if (args.Data is GroupIncomingMessage group)
            {
                if (!current.TargetGroupIds.Contains(group.Group.GroupId) || group.GroupMember.UserId == login.Uin) return;
                var mentioned = group.Segments.OfType<IncomingSegment<MentionIncomingSegmentData>>().Any(s => s.Data.UserId == login.Uin);
                if (current.ReplyOnlyWhenMentioned && !mentioned) return;
                var text = ExtractText(group.Segments, login.Uin);
                if (string.IsNullOrWhiteSpace(text)) return;
                var member = group.GroupMember;
                var senderName = string.IsNullOrWhiteSpace(member?.Card) ? member?.Nickname : member?.Card;
                _logger?.Info("qq",
                    $"[recv] group={group.Group.GroupId} from={senderName}({group.GroupMember.UserId}): {Truncate(text)}");
                if (text.StartsWith('/'))
                {
                    var reply = await TryRunCommandAsync(text, current, group.GroupMember.UserId, group.Group.GroupId, isGroup: true, ct);
                    if (reply is not null)
                    {
                        _logger?.Info("qq", $"[send] group={group.Group.GroupId}: {Truncate(reply)}");
                        await milky.Message.SendGroupMessageAsync(new SendGroupMessageRequest(group.Group.GroupId, [new OutgoingSegment<TextOutgoingSegmentData>(new TextOutgoingSegmentData(reply))]), ct);
                    }
                    return;
                }
                var result = await new QqRuntimeBridge(_runtime, current).HandleAsync("group", group.Group.GroupId, group.GroupMember.UserId, text, senderName, ct);
                var rendered = QqRuntimeBridge.Render(result);
                var segments = ToSegments(rendered);
                if (segments.Count > 0)
                {
                    _logger?.Info("qq", $"[send] group={group.Group.GroupId}: {Truncate(string.Join(" | ", rendered.Select(p => p.Kind == "text" ? p.Value : $"[{p.Kind}]")))}");
                    await milky.Message.SendGroupMessageAsync(new SendGroupMessageRequest(group.Group.GroupId, [.. segments]), ct);
                }
            }
            else if (args.Data is FriendIncomingMessage friend)
            {
                if (!current.TargetFriendIds.Contains(friend.Friend.UserId) || friend.SenderId == login.Uin) return;
                var text = ExtractText(friend.Segments, null);
                if (string.IsNullOrWhiteSpace(text)) return;
                var friendName = string.IsNullOrWhiteSpace(friend.Friend.Nickname) ? friend.Friend.Remark : friend.Friend.Nickname;
                _logger?.Info("qq",
                    $"[recv] friend={friend.Friend.UserId} from={friendName}: {Truncate(text)}");
                if (text.StartsWith('/'))
                {
                    var reply = await TryRunCommandAsync(text, current, friend.Friend.UserId, friend.Friend.UserId, isGroup: false, ct);
                    if (reply is not null)
                    {
                        _logger?.Info("qq", $"[send] friend={friend.Friend.UserId}: {Truncate(reply)}");
                        await milky.Message.SendPrivateMessageAsync(new SendPrivateMessageRequest(friend.Friend.UserId,
                            [new OutgoingSegment<TextOutgoingSegmentData>(new TextOutgoingSegmentData(reply))]), ct);
                    }
                    return;
                }
                var result = await new QqRuntimeBridge(_runtime, current).HandleAsync("friend", friend.Friend.UserId, friend.SenderId, text, friendName, ct);
                var rendered = QqRuntimeBridge.Render(result);
                var segments = ToSegments(rendered);
                if (segments.Count > 0)
                {
                    _logger?.Info("qq", $"[send] friend={friend.Friend.UserId}: {Truncate(string.Join(" | ", rendered.Select(p => p.Kind == "text" ? p.Value : $"[{p.Kind}]")))}");
                    await milky.Message.SendPrivateMessageAsync(new SendPrivateMessageRequest(friend.Friend.UserId, [.. segments]), ct);
                }
            }
        };
        await milky.ReceivingEventUsingWebSocketAsync(static ws => ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30), ct);
    }

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
}
