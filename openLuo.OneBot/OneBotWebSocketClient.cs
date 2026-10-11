using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace openLuo.OneBot;

/// <summary>
/// OneBot 11 正向 WebSocket 客户端(LLBot/LLOneBot 等协议端)。
/// 职责纯协议:连接/重连、事件接收、action 发送与 echo 路由、心跳保活。
/// 不含任何 openLuo 业务依赖。
/// </summary>
public sealed class OneBotWebSocketClient : IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly string? _accessToken;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _runCts;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>收到消息事件(已解析为统一事件模型)。</summary>
    public event Action<OneBotMessageEvent>? MessageReceived;

    /// <summary>连接状态变化(文本,供宿主日志;connected/closed/reconnecting)。</summary>
    public event Action<string>? ConnectionStateChanged;

    public OneBotWebSocketClient(Uri uri, ILogger? logger = null, string? accessToken = null)
    {
        _uri = uri;
        _accessToken = accessToken;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>持续运行:连接→接收循环→断线指数退避重连(1s→30s),直到 ct 取消。</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(_runCts.Token);
                retryDelay = TimeSpan.FromSeconds(1);
                if (ct.IsCancellationRequested)
                    return;
                _logger.LogWarning("OneBot WebSocket ended without exception; reconnecting.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError("OneBot WebSocket failed: {Message}; reconnect in {Delay}s", ex.Message, retryDelay.TotalSeconds);
            }
            try
            {
                await Task.Delay(retryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
        }
    }

    private async Task ConnectAndReceiveAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        // 回环地址一律直连：系统级 http_proxy 会让 ws://localhost:3001（LLBot）绕行代理，
        // 代理抖动/重启即掉线，且故障表现为"消息没发出去"。与 HubClient 同一约定（docs §6）。
        if (_uri.IsLoopback)
            ws.Options.Proxy = null;
        if (!string.IsNullOrWhiteSpace(_accessToken))
            ws.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
        await ws.ConnectAsync(_uri, ct);
        _ws = ws;
        ConnectionStateChanged?.Invoke("connected");
        _logger.LogInformation("OneBot WebSocket connected: {Uri}", _uri);

        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("OneBot WebSocket closed by peer.");
                        ConnectionStateChanged?.Invoke("closed");
                        return;
                    }
                    if (result.MessageType == WebSocketMessageType.Binary)
                        continue;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                var json = sb.ToString();
                sb.Clear();
                if (string.IsNullOrWhiteSpace(json))
                    continue;
                try
                {
                    HandleFrame(JsonNode.Parse(json) as JsonObject);
                }
                catch (Exception ex)
                {
                    _logger.LogError("OneBot frame handling failed: {Message}", ex.Message);
                }
            }
        }
        finally
        {
            // 连接结束（正常关闭/异常 abort）：清掉引用，避免后续 CallApiAsync 对着死 socket 发送
            if (ReferenceEquals(_ws, ws))
                _ws = null;
        }
    }

    private void HandleFrame(JsonObject? root)
    {
        if (root is null)
            return;
        // 响应帧:带 echo 且匹配 pending
        if (root["echo"]?.GetValue<string>() is { Length: > 0 } echo
            && _pending.TryRemove(echo, out var tcs))
        {
            tcs.TrySetResult(root);
            return;
        }
        // 事件帧:meta(心跳/lifecycle)忽略;message → MessageReceived
        switch (root["post_type"]?.GetValue<string>())
        {
            case "message":
                if (OneBotMessageEvent.FromJson(root) is { } message)
                    MessageReceived?.Invoke(message);
                break;
            case "meta_event":
                break;   // heartbeat/lifecycle:连接保活由 KeepAlive/重连覆盖,无需处理
        }
    }

    /// <summary>发送 action 并等待 echo 响应。并发安全。</summary>
    public async Task<OneBotSendResult> SendActionAsync(string action, JsonObject? @params, CancellationToken ct = default)
    {
        var response = await CallApiAsync(action, @params, ct);
        return response is null
            ? new OneBotSendResult { Ok = false, Error = "not connected or no response" }
            : OneBotSendResult.FromJson(response);
    }

    /// <summary>通用 API 调用:返回响应根对象(含 data);未连接/超时返回 null。</summary>
    public async Task<JsonObject?> CallApiAsync(string action, JsonObject? @params, CancellationToken ct = default)
    {
        var echo = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[echo] = tcs;
        var frame = new JsonObject
        {
            ["action"] = action,
            ["params"] = @params ?? [],
            ["echo"] = echo
        };

        ClientWebSocket? ws;
        lock (this)
            ws = _ws;
        if (ws?.State != WebSocketState.Open)
        {
            _pending.TryRemove(echo, out _);
            return null;
        }

        try
        {
            await _sendGate.WaitAsync(ct);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(frame.ToJsonString());
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sendGate.Release();
            }
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(echo, out _);
            return null;
        }
        catch (Exception ex)
        {
            _pending.TryRemove(echo, out _);
            _logger.LogWarning("OneBot action {Action} failed: {Message}", action, ex.Message);
            return null;
        }
    }

    public Task<OneBotSendResult> SendPrivateMessageAsync(long userId, IReadOnlyList<OneBotSegment> segments, CancellationToken ct = default)
    {
        var @params = new JsonObject
        {
            ["user_id"] = userId,
            ["message"] = ToJsonArray(segments)
        };
        return SendActionAsync("send_private_msg", @params, ct);
    }

    public Task<OneBotSendResult> SendGroupMessageAsync(long groupId, IReadOnlyList<OneBotSegment> segments, CancellationToken ct = default)
    {
        var @params = new JsonObject
        {
            ["group_id"] = groupId,
            ["message"] = ToJsonArray(segments)
        };
        return SendActionAsync("send_group_msg", @params, ct);
    }

    private static JsonArray ToJsonArray(IReadOnlyList<OneBotSegment> segments)
    {
        var array = new JsonArray();
        foreach (var segment in segments)
            array.Add(segment.ToJson());
        return array;
    }

    public async ValueTask DisposeAsync()
    {
        _runCts?.Cancel();
        try
        {
            if (_ws is { } ws)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch
        {
            // 忽略关闭异常
        }
        _ws = null;
        _runCts?.Dispose();
        foreach (var (_, tcs) in _pending)
            tcs.TrySetCanceled();
        _pending.Clear();
    }
}
