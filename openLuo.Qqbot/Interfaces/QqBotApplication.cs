using System.Text.Json.Nodes;
using openLuo.Client;
using openLuo.OneBot;

namespace openLuo.Interfaces.QQbot;

/// <summary>
/// QQ bot 客户端（OneBot 11 通道）：把 OneBot 消息事件翻译为 Hub 会话语义（Observe / 命令 / 回合），
/// 把回合产物渲染为 OneBot 段发出。**不引用内核**——能力/记忆/状态都在 Hub 侧。
/// </summary>
public sealed class QqBotApplication
{
    private readonly Func<CancellationToken, Task<HubClient>> _hubFactory;
    private readonly HttpClient _http;
    private readonly string _httpBase;
    private readonly IQqBotConfigCenter _configCenter;
    private readonly Action<string>? _log;

    private const int LogTextMax = 300;
    private long _selfId;

    /// <summary>当前 Hub 连接（断线由 <see cref="EnsureHubAsync"/> 重建；进程内单例，长期存活）。</summary>
    private HubClient? _hub;
    private readonly SemaphoreSlim _hubGate = new(1, 1);
    private TimeSpan _hubRetryDelay = TimeSpan.FromSeconds(1);
    private DateTimeOffset _hubRetryAt;

    public QqBotApplication(
        Func<CancellationToken, Task<HubClient>> hubFactory,
        HttpClient http, string httpBase, IQqBotConfigCenter configCenter, Action<string>? log = null)
    {
        _hubFactory = hubFactory;
        _http = http;
        _httpBase = httpBase.TrimEnd('/');
        _configCenter = configCenter;
        _log = log;
    }

    /// <summary>
    /// 取一个可用的 Hub 连接：不可用（未连/已断）时重建，失败按 1s→30s 退避并在重建前等待退避窗口。
    /// 并发消息在 <see cref="_hubGate"/> 上串行化，避免同时建多条连接。
    /// </summary>
    private async Task<HubClient> EnsureHubAsync(CancellationToken ct)
    {
        if (_hub is { IsConnected: true } live)
            return live;

        await _hubGate.WaitAsync(ct);
        try
        {
            if (_hub is { IsConnected: true } current)
                return current;

            if (_hub is { } stale)
            {
                _hub = null;
                try { await stale.DisposeAsync(); } catch { /* 已断连：忽略 */ }
            }

            var wait = _hubRetryAt - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            try
            {
                var fresh = await _hubFactory(ct);
                _hub = fresh;
                _hubRetryDelay = TimeSpan.FromSeconds(1);
                _hubRetryAt = default;
                Log($"[hub] connected: protocol=v{fresh.Welcome?.ProtocolVersion}");
                return fresh;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _hubRetryDelay = TimeSpan.FromSeconds(Math.Min(_hubRetryDelay.TotalSeconds * 2, 30));
                _hubRetryAt = DateTimeOffset.UtcNow + _hubRetryDelay;
                Log($"[hub] connect failed: {ex.Message}（{_hubRetryDelay.TotalSeconds:0}s 后重试）");
                throw;
            }
        }
        finally
        {
            _hubGate.Release();
        }
    }

    /// <summary>执行一个回合并把片段按序发出；Hub 在**尚未产出任何片段**前断开时重连并重试一次。</summary>
    private async Task RunTurnAsync(
        OneBotWebSocketClient client, bool isGroup, long targetId, long actorId,
        string scene, string text, string? senderName, IReadOnlyList<JsonNode>? blocks,
        QqBotConfig config, string logLabel, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var hub = await EnsureHubAsync(ct);
            var bridge = new QqRuntimeBridge(hub, _http, _httpBase, config);
            var sent = 0;
            try
            {
                await foreach (var part in bridge.TurnAsync(scene, targetId, actorId, text, senderName, blocks, ct))
                {
                    await SendPartsAsync(client, isGroup, targetId, [part], config, logLabel, ct);
                    sent++;
                }
                return;
            }
            catch (HubDisconnectedException ex) when (attempt == 0 && sent == 0)
            {
                // 尚未发出任何片段 → 重试安全（不会重复回复）；EnsureHubAsync 会重建连接
                Log($"[warn] hub 断开（{ex.State}），重连后重试本回合 {logLabel}");
            }
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var config = _configCenter.GetSnapshot();
        if (!config.Enabled)
        {
            Log("QQbot is disabled. Set enabled=true in qqbot.jsonc.");
            return;
        }
        if (string.IsNullOrWhiteSpace(config.BaseAddress))
        {
            Log("QQbot config missing baseAddress (OneBot 正向 WebSocket 地址,如 ws://localhost:3001/).");
            return;
        }
        if (config.TargetGroupIds.Count == 0 && config.TargetFriendIds.Count == 0)
        {
            Log("QQbot config has no valid targets. Set targetGroupIds or targetFriendIds.");
            return;
        }

        var client = new OneBotWebSocketClient(new Uri(config.BaseAddress), accessToken: config.AccessToken, logger: null);

        client.ConnectionStateChanged += state =>
        {
            Log($"[onebot] {state}");
            if (state == "connected" && Interlocked.Read(ref _selfId) == 0)
                _ = RefreshSelfIdAsync(client, ct);
        };
        client.MessageReceived += ev => _ = HandleMessageAsync(client, ev, ct);

        Log($"[hub] OneBot={config.BaseAddress}");
        var warmup = WarmupHubAsync(ct);
        try
        {
            await client.RunAsync(ct);
        }
        finally
        {
            await warmup;
            if (_hub is { } hub)
            {
                try { await hub.DisposeAsync(); } catch { /* 已断连：忽略 */ }
                _hub = null;
            }
            await client.DisposeAsync();
        }
    }

    /// <summary>启动后后台连 Hub：失败退避重试直到连上（QQ 桥不应因为 Hub 还没起就退出）。</summary>
    private async Task WarmupHubAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EnsureHubAsync(ct);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 失败原因与下次重试时间已由 EnsureHubAsync 记录；退避也在其中等待
            }
        }
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
            Log($"[warn] get_login_info parse failed: {ex.Message}");
        }
    }

    private async Task HandleMessageAsync(OneBotWebSocketClient client, OneBotMessageEvent ev, CancellationToken ct)
    {
        try
        {
            var current = _configCenter.GetSnapshot();
            var selfId = Interlocked.Read(ref _selfId) != 0 ? _selfId : ev.SelfId;
            var isGroup = ev.MessageType == "group";

            if (isGroup)
            {
                if (!ev.GroupId.HasValue || !current.TargetGroupIds.Contains(ev.GroupId.Value) || ev.UserId == selfId)
                    return;

                var text = ev.ToPlainText(selfId);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var blocks = await DownloadImagesAsync(ev.ImageUrls(), ct);
                var senderName = ev.SenderDisplayName ?? string.Empty;
                LogMessage(current, $"[recv] group={ev.GroupId} from={senderName}({ev.UserId}): {Truncate(text)}");
                var logLabel = $"group={ev.GroupId}";

                if (current.ReplyOnlyWhenMentioned && !ev.Mentions(selfId))
                {
                    // 感知通道：未 @ 的消息写入会话历史（“看但不回”），不触发 LLM 回合
                    var observer = new QqRuntimeBridge(await EnsureHubAsync(ct), _http, _httpBase, current);
                    await observer.ObserveAsync("group", ev.GroupId.Value, text, senderName, blocks, ct);
                    return;
                }

                if (text.StartsWith('/'))
                {
                    var bridge = new QqRuntimeBridge(await EnsureHubAsync(ct), _http, _httpBase, current);
                    var reply = await TryRunCommandAsync(bridge, text, current, ev.UserId, ev.GroupId.Value, isGroup: true, ct);
                    if (reply is not null)
                        await SendTextAsync(client, isGroup: true, ev.GroupId.Value, reply, current, logLabel, ct);
                    return;
                }

                await RunTurnAsync(client, isGroup: true, ev.GroupId.Value, ev.UserId, "group", text, senderName, blocks, current, logLabel, ct);
            }
            else if (ev.MessageType == "private")
            {
                if (!current.TargetFriendIds.Contains(ev.UserId) || ev.UserId == selfId)
                    return;

                var text = ev.ToPlainText(null);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var blocks = await DownloadImagesAsync(ev.ImageUrls(), ct);
                var senderName = ev.SenderDisplayName ?? string.Empty;
                LogMessage(current, $"[recv] friend={ev.UserId} from={senderName}: {Truncate(text)}");
                var logLabel = $"friend={ev.UserId}";

                if (text.StartsWith('/'))
                {
                    var bridge = new QqRuntimeBridge(await EnsureHubAsync(ct), _http, _httpBase, current);
                    var reply = await TryRunCommandAsync(bridge, text, current, ev.UserId, ev.UserId, isGroup: false, ct);
                    if (reply is not null)
                        await SendTextAsync(client, isGroup: false, ev.UserId, reply, current, logLabel, ct);
                    return;
                }

                await RunTurnAsync(client, isGroup: false, ev.UserId, ev.UserId, "friend", text, senderName, blocks, current, logLabel, ct);
            }
        }
        catch (HubDisconnectedException ex)
        {
            // 与"处理消息出错"区分：Hub 连接断开（会自动重连），本条消息没能完成
            Log($"[error] hub 连接断开，本条消息未完成：{ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"[error] message handler failed: {ex.Message}");
        }
    }

    private async Task SendTextAsync(OneBotWebSocketClient client, bool isGroup, long targetId, string text, QqBotConfig config, string logLabel, CancellationToken ct)
    {
        LogMessage(config, $"[send] {logLabel}: {Truncate(text)}");
        var result = isGroup
            ? await client.SendGroupMessageAsync(targetId, [OneBotSegment.Text(text)], ct)
            : await client.SendPrivateMessageAsync(targetId, [OneBotSegment.Text(text)], ct);
        if (!result.Ok)
            Log($"[error] text send failed: {result.Error}");
    }

    /// <summary>渲染 parts → 按序发送若干条 OneBot 消息（text/image 合并一条，music/record 各自独占一条）。</summary>
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
                case "record" when ExtractBase64(part.Value) is { } audio:
                    segments.Add(OneBotSegment.Record($"base64://{audio}"));
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
                Log($"[error] message send failed: {result.Error}");
        }
    }

    /// <summary>单回合最多发出的音乐卡数；超出部分折叠为文本链接。</summary>
    internal const int MaxMusicCardsPerTurn = 3;

    /// <summary>把段列表拆成若干条可发送的消息：music/record 必须各自独占一条（OneBot→QQ 同消息多个只取第一个）。</summary>
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

        static bool IsExclusive(OneBotSegment s) => s.Type is "music" or "record";

        foreach (var seg in segments)
        {
            if (!IsExclusive(seg))
            {
                text.Add(seg);
                continue;
            }

            FlushText();
            if (seg.Type == "music")
            {
                if (cards.Count >= MaxMusicCardsPerTurn)
                {
                    overflow.Add(seg);
                    continue;
                }
                cards.Add(seg);
            }
            batches.Add([seg]);
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

    private static string SegmentToLog(OneBotSegment seg) => seg.Type switch
    {
        "text" => seg.Data["text"]?.GetValue<string>() ?? string.Empty,
        "music" => $"[music:{seg.Data["id"]}]",
        "record" => "[record]",
        _ => $"[{seg.Type}]"
    };

    /// <summary>平台命令分发：仅 admin 可执行；返回要发送的文本，null 表示不回复。</summary>
    private static async Task<string?> TryRunCommandAsync(QqRuntimeBridge bridge, string text, QqBotConfig config, long actorId, long channelId, bool isGroup, CancellationToken ct)
    {
        if (!config.AdminUsers.Contains(actorId))
            return "Permission Denied";

        var command = text.TrimStart('/').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        return command switch
        {
            "context" => await bridge.GetContextSummaryAsync(isGroup ? "group" : "friend", channelId, ct),
            "help" => "commands: /context /help",
            _ => $"unknown command: /{command}"
        };
    }

    /// <summary>下载图片并转为协议图像块（dataUri 供视觉模型消费）。失败/超时/超大图跳过，不阻塞消息处理。</summary>
    internal static async Task<IReadOnlyList<JsonNode>> DownloadImagesAsync(IEnumerable<string> urls, CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };
        var blocks = await Task.WhenAll(urls.Select(url => DownloadImageAsync(http, url, ct)));
        return blocks.Where(b => b is not null).Cast<JsonNode>().ToList();
    }

    private static async Task<JsonNode?> DownloadImageAsync(HttpClient http, string url, CancellationToken ct)
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
            const int maxBytes = 3 * 1024 * 1024;   // base64 后 +33%，接口载荷上限 4MB
            if (bytes.Length > maxBytes)
            {
                Log($"image download skipped (too large: {bytes.Length} bytes): {Truncate(url)}");
                return null;
            }

            return new JsonObject
            {
                ["kind"] = "image",
                ["mime"] = mime,
                ["name"] = url,
                ["assetId"] = url,
                ["dataUri"] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}",
            };
        }
        catch (Exception ex)
        {
            Log($"image download failed: {ex.Message} ({Truncate(url)})");
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

    private void LogMessage(QqBotConfig config, string message)
    {
        if (config.LogMessages)
            Log(message);
    }

    private static void Log(string message) => Console.Error.WriteLine($"[qq] {message}");
}
