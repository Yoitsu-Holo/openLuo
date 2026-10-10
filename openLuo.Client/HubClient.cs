using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using openLuo.Protocol;

namespace openLuo.Client;

/// <summary>Hub 返回的错误（携带 wire 错误码，见 §4.5）。</summary>
public sealed class HubException : Exception
{
    public int ErrorCode { get; }

    public HubException(Envelope envelope)
        : base($"hub error {envelope.ErrorCode} ({ErrorCodes.NameOf(envelope.ErrorCode)}): {envelope.ErrorMsg}")
        => ErrorCode = envelope.ErrorCode;
}

/// <summary>连接 Hub 失败：携带目标地址与可操作提示（Hub 是否已启动 / 代理是否可达）。</summary>
public sealed class HubConnectException : Exception
{
    public string StreamUrl { get; }

    public HubConnectException(string streamUrl, Exception inner)
        : base($"无法连接 Hub：{streamUrl}（{inner.Message.Split('\n')[0]}）。"
               + "请确认 Hub 已启动（./openLuo --serve），或设置 OPENLUO_HUB_URL 指向已运行的 Hub。", inner)
        => StreamUrl = streamUrl;
}

/// <summary>
/// Hub 客户端（WebSocket 数据面）。**只依赖 `openLuo.Protocol`**，不引用任何内核程序集（§2 边界规则）。
/// <para>MVP 为顺序请求-响应：同一时刻只应有一个进行中的请求（<see cref="StreamTurnAsync"/> 或
/// <see cref="OpenSessionAsync"/>）；并发调用需上层串行化。</para>
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly HttpClient _http;
    private string _httpBase = string.Empty;
    private string? _token;

    private HubClient(ClientWebSocket socket, HttpClient http)
    {
        _socket = socket;
        _http = http;
    }

    /// <summary>协商结果（连接后可用）。</summary>
    public WelcomeEvent? Welcome { get; private set; }

    /// <summary>连接并完成 `hello` / `welcome` 协商。</summary>
    public static async Task<HubClient> ConnectAsync(
        string streamUrl,
        string clientId,
        string clientType,
        string? token = null,
        IReadOnlyList<string>? features = null,
        CancellationToken ct = default)
    {
        var uri = new Uri(streamUrl);

        // 回环地址一律直连：系统级 http_proxy/https_proxy 会让本地 ws:// 与 Hub HTTP 请求绕行代理
        // （代理不可达该端口时表现为 "response ended prematurely" / "Unable to connect"，极难定位）。
        var socket = new ClientWebSocket();
        if (uri.IsLoopback)
            socket.Options.Proxy = null;

        try
        {
            await socket.ConnectAsync(uri, ct);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException)
        {
            socket.Dispose();
            throw new HubConnectException(streamUrl, ex);
        }

        var client = new HubClient(socket, new HttpClient(new SocketsHttpHandler { UseProxy = !uri.IsLoopback }));

        // token 未显式给出时，回退环境变量（OPENLUO_HUB_TOKEN），便于各客户端统一鉴权
        token ??= Environment.GetEnvironmentVariable("OPENLUO_HUB_TOKEN");

        await client.SendAsync(EnvelopeFactory.Create(MessageTypes.Hello, new HelloCommand
        {
            ProtocolVersion = ProtocolInfo.MajorVersion,
            ClientId = clientId,
            ClientType = clientType,
            Token = token,
            Features = features ?? [],
        }), ct);

        var reply = await client.ReceiveAsync(ct)
            ?? throw new IOException("hub closed connection during handshake");

        if (reply.Type != EventTypes.Welcome)
            throw reply.IsError ? new HubException(reply) : new InvalidOperationException($"expected welcome, got '{reply.Type}'");

        client.Welcome = reply.DataAs<WelcomeEvent>();
        client._httpBase = ToHttpBase(streamUrl);
        client._token = token;
        return client;
    }

    /// <summary>由数据面地址推导控制面基址（ws://host:port/v1/stream → http://host:port）。</summary>
    private static string ToHttpBase(string streamUrl)
    {
        var text = streamUrl.Replace("ws://", "http://", StringComparison.Ordinal)
                            .Replace("wss://", "https://", StringComparison.Ordinal);
        var marker = text.IndexOf("/v1/", StringComparison.Ordinal);
        return marker < 0 ? text.TrimEnd('/') : text[..marker];
    }

    /// <summary>开启会话（等价 HTTP `POST /v1/sessions`）。</summary>
    public async Task<SessionDto> OpenSessionAsync(
        string subjectId, string agentId, string? conversationId = null, CancellationToken ct = default)
    {
        var request = EnvelopeFactory.Create(MessageTypes.SessionOpen, new SessionOpenCommand
        {
            SubjectId = subjectId,
            AgentId = agentId,
            ConversationId = conversationId,
        });

        await SendAsync(request, ct);

        var reply = await ReceiveAsync(ct) ?? throw new IOException("hub closed connection");
        if (reply.IsError)
            throw new HubException(reply);

        return reply.DataAs<SessionDto>() ?? throw new InvalidOperationException("empty session payload");
    }

    /// <summary>
    /// 提交回合并流式消费事件（真流式）：`turn.accepted` → `decision`/`tool.call`/`tool.result`/`output`
    /// → `turn.final`（或 `error` 终止）。由调用方按 Envelope.Type 分派。
    /// </summary>
    public async IAsyncEnumerable<Envelope> StreamTurnAsync(
        TurnRequestDto request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await SendAsync(EnvelopeFactory.Create(MessageTypes.TurnSubmit, request), ct);

        while (true)
        {
            var evt = await ReceiveAsync(ct);
            if (evt is null)
                yield break;

            yield return evt;

            if (evt.Type is EventTypes.TurnFinal or EventTypes.Error)
                yield break;
        }
    }

    public Task SendAsync(Envelope envelope, CancellationToken ct = default)
        => _socket.SendAsync(Encoding.UTF8.GetBytes(ProtocolJson.Serialize(envelope)), WebSocketMessageType.Text, true, ct);

    /// <summary>读取一条 Envelope（合并分片帧）；连接关闭返回 null。</summary>
    public async Task<Envelope?> ReceiveAsync(CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(_buffer, ct);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType == WebSocketMessageType.Binary)
                continue;

            ms.Write(_buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return ProtocolJson.Deserialize<Envelope>(Encoding.UTF8.GetString(ms.ToArray()));
    }

    /// <summary>上传资产（HTTP `POST /v1/assets`），返回引用（§9）。</summary>
    public async Task<AssetRefDto?> UploadAssetAsync(
        byte[] bytes, string mime, string? fileName = null, string? sessionId = null, CancellationToken ct = default)
    {
        var query = sessionId is null ? string.Empty : $"?sessionId={Uri.EscapeDataString(sessionId)}";

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        if (!string.IsNullOrWhiteSpace(fileName))
            content.Headers.Add("X-File-Name", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_httpBase}/v1/assets{query}") { Content = content };
        Authorize(request);

        using var response = await _http.SendAsync(request, ct);
        var envelope = ProtocolJson.Deserialize<Envelope>(await response.Content.ReadAsStringAsync(ct));
        return envelope?.DataAs<UploadAssetResponse>()?.Asset;
    }

    /// <summary>下载资产字节（HTTP `GET /v1/assets/{id}`）。</summary>
    public async Task<byte[]?> DownloadAssetAsync(string assetId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_httpBase}/v1/assets/{assetId}");
        Authorize(request);

        using var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    private void Authorize(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // 忽略关闭异常
        }
        _socket.Dispose();
    }
}
