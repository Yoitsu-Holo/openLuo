using System.Globalization;
using Microsoft.Extensions.Logging;
using Milky.Net.Client;
using Milky.Net.Model;
using openLuo.Core.Models;
using openLuo.Hosting;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Interfaces.QQbot;

public sealed class QqBotApplication
{
    private readonly IGameSessionCatalog _sessionCatalog;
    private readonly IQqBotConfigCenter _configCenter;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly Dictionary<string, IGameSession> _targetSessions = new(StringComparer.OrdinalIgnoreCase);

    public QqBotApplication(
        IGameSessionCatalog sessionCatalog,
        IQqBotConfigCenter configCenter)
    {
        _sessionCatalog = sessionCatalog;
        _configCenter = configCenter;
        _logger = BootstrapLogger.Create<QqBotApplication>();
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var config = _configCenter.GetSnapshot();
        if (!config.Enabled)
        {
            _logger.LogError("QQbot 未启用。请在 qqbot.jsonc 中设置 enabled=true。");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.BaseAddress))
        {
            _logger.LogError("QQbot 配置缺少 qqBot.baseAddress。");
            return;
        }

        var targetGroupIds = BuildTargetIdSet(config.TargetGroupIds);
        var targetFriendIds = BuildTargetIdSet(config.TargetFriendIds);

        if (targetGroupIds.Count == 0 && targetFriendIds.Count == 0)
        {
            _logger.LogError("QQbot 配置缺少有效目标。请设置 qqBot.targetGroupIds 或 qqBot.targetFriendIds。");
            return;
        }

        using HttpClient client = new() { BaseAddress = new Uri(config.BaseAddress) };
        MilkyClient milky = new(client);
        var loginInfo = await milky.System.GetLoginInfoAsync(ct);
        _logger.LogInformation(
            "QQbot 已连接，targetGroups={GroupIds}，targetFriends={FriendIds}，botUin={BotUin}",
            targetGroupIds.Count == 0 ? "<empty>" : string.Join(",", targetGroupIds),
            targetFriendIds.Count == 0 ? "<empty>" : string.Join(",", targetFriendIds),
            loginInfo.Uin);

        milky.Events.MessageReceive += async (_, args) =>
        {
            var latestConfig = _configCenter.GetSnapshot();
            var latestTargetGroupIds = BuildTargetIdSet(latestConfig.TargetGroupIds);
            var latestTargetFriendIds = BuildTargetIdSet(latestConfig.TargetFriendIds);

            if (args.Data is GroupIncomingMessage { Group.GroupId: long groupId } groupMessage)
            {
                if (!latestTargetGroupIds.Contains(groupId))
                    return;
                if (groupMessage.GroupMember.UserId == loginInfo.Uin)
                    return;

                await HandleGroupMessageAsync(milky, groupMessage, loginInfo.Uin, latestConfig, ct);
                return;
            }

            if (args.Data is FriendIncomingMessage friendMessage)
            {
                if (!latestTargetFriendIds.Contains(friendMessage.Friend.UserId))
                    return;
                if (friendMessage.SenderId == loginInfo.Uin)
                    return;

                await HandleFriendMessageAsync(milky, friendMessage, latestConfig, ct);
            }
        };

        await milky.ReceivingEventUsingWebSocketAsync(
            static ws => { ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30); },
            ct);
    }

    private async Task HandleGroupMessageAsync(
        MilkyClient milky,
        GroupIncomingMessage message,
        long botUserId,
        QqBotConfig config,
        CancellationToken ct)
    {
        var normalizedWithImages = QqMessageNormalizer.NormalizeWithImages(message, botUserId);
        var normalized = normalizedWithImages.Text;
        var mentioned = QqMessageNormalizer.MentionsBot(message, botUserId);
        var routedInput = RouteGroupInput(normalized, mentioned, config);
        var imageParts = config.EnableImageToLlm ? await DownloadImagePartsAsync(normalizedWithImages.Images, ct) : [];

        if (routedInput.IsEmpty)
        {
            if (!mentioned && ShouldRecordAmbientGroupMessage(normalized))
            {
                var session = await GetOrCreateTargetSessionAsync("group", message.Group.GroupId.ToString(CultureInfo.InvariantCulture), config, ct);
                await session.SubmitAsync(new GameSessionInput
                {
                    SourceId = "qqbot",
                    ChannelId = $"qq-group:{message.Group.GroupId}",
                    ActorId = $"qq:{message.GroupMember.UserId}",
                    Kind = SessionInputKind.Ambient,
                    Text = normalized,
                    Parts = imageParts,
                    Origin = BuildOrigin(
                        scene: "group",
                        conversationId: message.Group.GroupId.ToString(CultureInfo.InvariantCulture),
                        userId: message.GroupMember.UserId.ToString(CultureInfo.InvariantCulture),
                        userDisplayName: message.GroupMember.Nickname,
                        mentioned: false,
                        isDirectMessage: false),
                    PresentationProfile = SessionPresentationProfile.InstantMessageCompact,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["platform"] = "qq",
                        ["qq.groupId"] = message.Group.GroupId.ToString(CultureInfo.InvariantCulture),
                        ["qq.userId"] = message.GroupMember.UserId.ToString(CultureInfo.InvariantCulture),
                        ["qq.nickname"] = message.GroupMember.Nickname ?? string.Empty
                    }
                }, ct);
            }
            return;
        }

        await _requestLock.WaitAsync(ct);
        try
        {
            _logger.LogInformation(
                "QQbot 收到群消息：group={GroupId} user={UserId} mentioned={Mentioned} input={Input}",
                message.Group.GroupId,
                message.GroupMember.UserId,
                mentioned,
                routedInput.DisplayText);

            if (IsCommandDenied(routedInput, message.GroupMember.UserId, config))
            {
                MirrorReplyToConsole("group", message.Group.GroupId.ToString(CultureInfo.InvariantCulture), "Permission Denied");
                await milky.Message.SendGroupMessageAsync(
                    new SendGroupMessageRequest(
                        message.Group.GroupId,
                        [
                            new OutgoingSegment<MentionOutgoingSegmentData>(
                                new MentionOutgoingSegmentData(message.GroupMember.UserId)),
                            new OutgoingSegment<TextOutgoingSegmentData>(
                                new TextOutgoingSegmentData(" Permission Denied"))
                        ]),
                    ct);
                return;
            }

            var session = await GetOrCreateTargetSessionAsync("group", message.Group.GroupId.ToString(CultureInfo.InvariantCulture), config, ct);
            var result = await session.SubmitAsync(new GameSessionInput
            {
                SourceId = "qqbot",
                ChannelId = $"qq-group:{message.Group.GroupId}",
                ActorId = $"qq:{message.GroupMember.UserId}",
                Kind = routedInput.Kind,
                Text = routedInput.Text,
                Command = routedInput.Command,
                Parts = imageParts,
                Origin = BuildOrigin(
                    scene: "group",
                    conversationId: message.Group.GroupId.ToString(CultureInfo.InvariantCulture),
                    userId: message.GroupMember.UserId.ToString(CultureInfo.InvariantCulture),
                    userDisplayName: message.GroupMember.Nickname,
                    mentioned: mentioned,
                    isDirectMessage: false),
                PresentationProfile = SessionPresentationProfile.InstantMessageCompact,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["platform"] = "qq",
                    ["qq.groupId"] = message.Group.GroupId.ToString(CultureInfo.InvariantCulture),
                    ["qq.userId"] = message.GroupMember.UserId.ToString(CultureInfo.InvariantCulture),
                    ["qq.nickname"] = message.GroupMember.Nickname ?? string.Empty
                }
            }, ct);

            var outbound = await BuildOutboundMessageAsync(session, result.Events, config, prependSpace: true, ct);
            if (outbound.Segments.Count == 0)
                return;

            MirrorReplyToConsole("group", message.Group.GroupId.ToString(CultureInfo.InvariantCulture), outbound.ConsoleText);

            List<OutgoingSegment> segments =
            [
                new OutgoingSegment<MentionOutgoingSegmentData>(
                    new MentionOutgoingSegmentData(message.GroupMember.UserId)),
                .. outbound.Segments
            ];

            await milky.Message.SendGroupMessageAsync(
                new SendGroupMessageRequest(message.Group.GroupId, [.. segments]),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "QQbot 处理群消息失败。");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task HandleFriendMessageAsync(
        MilkyClient milky,
        FriendIncomingMessage message,
        QqBotConfig config,
        CancellationToken ct)
    {
        var normalizedWithImages = QqMessageNormalizer.NormalizeWithImages(message);
        var normalized = normalizedWithImages.Text;
        var routedInput = RouteFriendInput(normalized, config);
        var imageParts = config.EnableImageToLlm ? await DownloadImagePartsAsync(normalizedWithImages.Images, ct) : [];

        if (routedInput.IsEmpty)
            return;

        await _requestLock.WaitAsync(ct);
        try
        {
            _logger.LogInformation(
                "QQbot 收到好友消息：friend={FriendId} sender={SenderId} input={Input}",
                message.Friend.UserId,
                message.SenderId,
                routedInput.DisplayText);

            if (IsCommandDenied(routedInput, message.SenderId, config))
            {
                MirrorReplyToConsole("friend", message.Friend.UserId.ToString(CultureInfo.InvariantCulture), "Permission Denied");
                await milky.Message.SendPrivateMessageAsync(
                    new SendPrivateMessageRequest(
                        message.Friend.UserId,
                        [
                            new OutgoingSegment<TextOutgoingSegmentData>(
                                new TextOutgoingSegmentData("Permission Denied"))
                        ]),
                    ct);
                return;
            }

            var session = await GetOrCreateTargetSessionAsync("friend", message.Friend.UserId.ToString(CultureInfo.InvariantCulture), config, ct);
            var result = await session.SubmitAsync(new GameSessionInput
            {
                SourceId = "qqbot",
                ChannelId = $"qq-friend:{message.Friend.UserId}",
                ActorId = $"qq:{message.SenderId}",
                Kind = routedInput.Kind,
                Text = routedInput.Text,
                Command = routedInput.Command,
                Parts = imageParts,
                Origin = BuildOrigin(
                    scene: "friend",
                    conversationId: message.Friend.UserId.ToString(CultureInfo.InvariantCulture),
                    userId: message.SenderId.ToString(CultureInfo.InvariantCulture),
                    userDisplayName: message.Friend.Nickname,
                    mentioned: true,
                    isDirectMessage: true),
                PresentationProfile = SessionPresentationProfile.InstantMessageCompact,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["platform"] = "qq",
                    ["qq.scene"] = "friend",
                    ["qq.userId"] = message.Friend.UserId.ToString(CultureInfo.InvariantCulture),
                    ["qq.senderId"] = message.SenderId.ToString(CultureInfo.InvariantCulture),
                    ["qq.nickname"] = message.Friend.Nickname ?? string.Empty
                }
            }, ct);

            var outbound = await BuildOutboundMessageAsync(session, result.Events, config, prependSpace: false, ct);
            if (outbound.Segments.Count == 0)
                return;

            MirrorReplyToConsole("friend", message.Friend.UserId.ToString(CultureInfo.InvariantCulture), outbound.ConsoleText);

            await milky.Message.SendPrivateMessageAsync(
                new SendPrivateMessageRequest(message.Friend.UserId, [.. outbound.Segments]),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "QQbot 处理好友消息失败。");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static async Task<IReadOnlyList<SessionInputPart>> DownloadImagePartsAsync(
        IReadOnlyList<QqImagePart> images,
        CancellationToken ct)
    {
        if (images.Count == 0)
            return [];

        var parts = new List<SessionInputPart>(images.Count);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var logger = BootstrapLogger.Create<QqBotApplication>();

        foreach (var img in images)
        {
            byte[]? data = null;
            try
            {
                data = await http.GetByteArrayAsync(img.TempUrl, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "QQ image download failed: {Url}", img.TempUrl);
                continue;
            }

            if (data is { Length: > 0 })
            {
                parts.Add(new SessionInputPart
                {
                    Kind = SessionContentKind.Binary,
                    Name = img.Summary ?? "qq_image",
                    MediaType = "image/jpeg",
                    Data = data,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["source"] = "qq_image",
                        ["tempUrl"] = img.TempUrl,
                        ["width"] = img.Width.ToString(CultureInfo.InvariantCulture),
                        ["height"] = img.Height.ToString(CultureInfo.InvariantCulture)
                    }
                });
            }
        }

        return parts;
    }

    private static SessionInputDescriptor RouteGroupInput(string normalized, bool mentioned, QqBotConfig config)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return SessionInputDescriptor.Empty;

        if (config.ReplyOnlyWhenMentioned && !mentioned)
            return SessionInputDescriptor.Empty;

        if (config.CommandPrefixPassthrough && normalized.StartsWith('/'))
            return SessionInputDescriptor.FromCommand(normalized);

        return SessionInputDescriptor.FromChat(normalized);
    }

    private static bool ShouldRecordAmbientGroupMessage(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.StartsWith('/'))
            return false;

        return true;
    }

    private static SessionInputDescriptor RouteFriendInput(string normalized, QqBotConfig config)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return SessionInputDescriptor.Empty;

        if (config.CommandPrefixPassthrough && normalized.StartsWith('/'))
            return SessionInputDescriptor.FromCommand(normalized);

        return SessionInputDescriptor.FromChat(normalized);
    }

    private static bool IsCommandDenied(SessionInputDescriptor input, long userId, QqBotConfig config)
    {
        if (input.Command is null)
            return false;

        return !config.AdminUsers.Contains(userId);
    }

    private static HashSet<long> BuildTargetIdSet(IEnumerable<long>? ids)
    {
        return ids?
            .Where(static id => id > 0)
            .ToHashSet()
            ?? [];
    }

    private async Task<QqOutboundMessage> BuildOutboundMessageAsync(
        IGameSession session,
        IReadOnlyList<GameEvent> events,
        QqBotConfig config,
        bool prependSpace,
        CancellationToken ct)
    {
        var segments = new List<OutgoingSegment>();
        var consoleParts = new List<string>();
        var hasMessageOutput = events.Any(static e => e is MessageEvent);

        foreach (var gameEvent in events)
        {
            switch (gameEvent)
            {
                case MessageEvent message:
                    foreach (var block in message.Blocks)
                    {
                        switch (block)
                        {
                            case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                            {
                                if (!config.IncludeStateSummaryInReply && text.Visibility == OutputVisibility.StateSummary)
                                    break;

                                var filtered = text.Text.Trim();
                                if (string.IsNullOrWhiteSpace(filtered))
                                    break;

                                if (prependSpace && segments.Count == 0)
                                    filtered = $" {filtered}";

                                segments.Add(new OutgoingSegment<TextOutgoingSegmentData>(
                                    new TextOutgoingSegmentData(filtered)));
                                consoleParts.Add(filtered.Trim());
                                break;
                            }
                            case ImageBlock image:
                            {
                                var blob = await session.GetAssetBlobAsync(image.AssetId, "primary", ct);
                                if (blob is null)
                                {
                                    var failed = $"[图片发送失败] assetId={image.AssetId}";
                                    if (prependSpace && segments.Count == 0)
                                        failed = $" {failed}";
                                    segments.Add(new OutgoingSegment<TextOutgoingSegmentData>(
                                        new TextOutgoingSegmentData(failed)));
                                    consoleParts.Add(failed.Trim());
                                    break;
                                }

                                segments.Add(new OutgoingSegment<ImageOutgoingSegmentData>(
                                    new ImageOutgoingSegmentData(
                                        new MilkyUri($"base64://{Convert.ToBase64String(blob.Data)}"),
                                        image.Name ?? "image",
                                        SubType.Normal)));
                                consoleParts.Add($"[图片] assetId={image.AssetId} mime={image.MimeType}");

                                if (!string.IsNullOrWhiteSpace(image.Caption))
                                {
                                    segments.Add(new OutgoingSegment<TextOutgoingSegmentData>(
                                        new TextOutgoingSegmentData($"\n{image.Caption.Trim()}")));
                                    consoleParts.Add(image.Caption.Trim());
                                }

                                break;
                            }
                            case AssetBlock asset:
                            {
                                var unsupported = $"[暂不支持的媒体] assetId={asset.AssetId} mime={asset.MimeType}";
                                if (prependSpace && segments.Count == 0)
                                    unsupported = $" {unsupported}";
                                segments.Add(new OutgoingSegment<TextOutgoingSegmentData>(
                                    new TextOutgoingSegmentData(unsupported)));
                                consoleParts.Add(unsupported.Trim());
                                break;
                            }
                        }
                    }
                    break;
                case TextOutputEvent text:
                    if (!hasMessageOutput && !string.IsNullOrWhiteSpace(text.Text))
                    {
                        if (!config.IncludeStateSummaryInReply && text.Visibility == OutputVisibility.StateSummary)
                            break;

                        var filtered = text.Text.Trim();
                        if (!string.IsNullOrWhiteSpace(filtered))
                        {
                            if (prependSpace && segments.Count == 0)
                                filtered = $" {filtered}";
                            segments.Add(new OutgoingSegment<TextOutgoingSegmentData>(
                                new TextOutgoingSegmentData(filtered)));
                            consoleParts.Add(filtered.Trim());
                        }
                    }
                    break;
                case ErrorEvent error:
                    if (!string.IsNullOrWhiteSpace(error.Error))
                    {
                        var errorText = $"[错误] {error.Error.Trim()}";
                        if (prependSpace && segments.Count == 0)
                            errorText = $" {errorText}";
                        segments.Add(new OutgoingSegment<TextOutgoingSegmentData>(
                            new TextOutgoingSegmentData(errorText)));
                        consoleParts.Add(errorText.Trim());
                    }
                    break;
            }
        }

        return new QqOutboundMessage
        {
            Segments = segments,
            ConsoleText = string.Join("\n", consoleParts.Where(static text => !string.IsNullOrWhiteSpace(text)))
        };
    }

    private async Task<IGameSession> GetOrCreateTargetSessionAsync(
        string scene,
        string targetId,
        QqBotConfig config,
        CancellationToken ct)
    {
        var targetKey = $"{scene}:{targetId}";
        if (_targetSessions.TryGetValue(targetKey, out var existing))
            return existing;

        await _sessionLock.WaitAsync(ct);
        try
        {
            if (_targetSessions.TryGetValue(targetKey, out existing))
                return existing;

            var gameId = BuildStableGameId(scene, targetId);
            var session = await _sessionCatalog.OpenGameSessionAsync(
                gameId,
                "qqbot",
                $"qqbot-{scene}-{targetId}",
                ct: ct);

            if (config.AutoInitializeSession)
            {
                if (string.IsNullOrWhiteSpace(config.DefaultArchetypeId))
                    throw new InvalidOperationException("QQbot 自动初始化需要配置 qqBot.defaultArchetypeId。");

                var state = await session.TryGetStateAsync(ct);
                if (state is null)
                {
                    var createdGameId = await session.InitGameAsync(
                        config.DefaultArchetypeId,
                        string.IsNullOrWhiteSpace(config.PlayerName) ? "群友" : config.PlayerName,
                        gameId,
                        ct);

                    _logger.LogInformation(
                        "QQbot 已自动初始化 target={TargetKey} session={SessionId} gameId={GameId} archetype={ArchetypeId}",
                        targetKey,
                        session.SessionId,
                        createdGameId,
                        config.DefaultArchetypeId);
                }
            }

            _targetSessions[targetKey] = session;
            return session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static string BuildStableGameId(string scene, string targetId) => $"qq-{scene}-{targetId}";

    private static SessionInputOrigin BuildOrigin(
        string scene,
        string conversationId,
        string userId,
        string? userDisplayName,
        bool mentioned,
        bool isDirectMessage)
    {
        return new SessionInputOrigin
        {
            Platform = "qq",
            Scene = scene,
            ConversationId = conversationId,
            UserId = userId,
            UserDisplayName = userDisplayName,
            MentionedAgent = mentioned,
            IsDirectMessage = isDirectMessage
        };
    }

    private void MirrorReplyToConsole(string scene, string targetId, string replyText)
    {
        Console.WriteLine($"[QQ/{scene}:{targetId}] {replyText}");
        _logger.LogInformation("QQbot 回复：scene={Scene} target={TargetId} reply={Reply}", scene, targetId, replyText);
    }

    private sealed class QqOutboundMessage
    {
        public required List<OutgoingSegment> Segments { get; init; }

        public string ConsoleText { get; init; } = string.Empty;
    }

    private sealed class SessionInputDescriptor
    {
        public static SessionInputDescriptor Empty { get; } = new() { Kind = SessionInputKind.System };

        public required SessionInputKind Kind { get; init; }

        public string? Text { get; init; }

        public SessionCommandInvocation? Command { get; init; }

        public bool IsEmpty => Kind == SessionInputKind.System && string.IsNullOrWhiteSpace(Text) && Command is null;

        public string DisplayText => Command?.RawText ?? Text ?? string.Empty;

        public static SessionInputDescriptor FromChat(string text) => new()
        {
            Kind = SessionInputKind.Chat,
            Text = text.Trim()
        };

        public static SessionInputDescriptor FromCommand(string normalized)
        {
            var parts = normalized[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var args = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < parts.Length)
                    options[parts[i][2..]] = parts[++i];
                else
                    args.Add(parts[i]);
            }

            return new SessionInputDescriptor
            {
                Kind = SessionInputKind.Command,
                Command = new SessionCommandInvocation
                {
                    Name = parts[0],
                    Prefix = normalized[0],
                    RawText = normalized,
                    Args = args,
                    Options = options
                }
            };
        }
    }
}
