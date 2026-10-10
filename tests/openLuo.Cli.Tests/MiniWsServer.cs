using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace openLuo.Cli.Tests;

/// <summary>
/// 测试用极简 WebSocket 服务端桩：手写握手 + 帧读写。
/// 之所以不用 <c>HttpListener</c>：本仓库最关心的场景是**连接被硬断**（RST → 客户端 socket 进入
/// <c>WebSocketState.Aborted</c>）——那是"Hub 重启/链路被掐"的真实表现，而 <c>WebSocket.Abort()</c>
/// 只能做到正常关闭，无法稳定复现 Aborted。
/// </summary>
internal sealed class MiniWsServer : IAsyncDisposable
{
    internal const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly Func<MiniWsConnection, string, Task> _handler;
    private readonly List<MiniWsConnection> _connections = [];
    private int _connectionCount;

    public MiniWsServer(Func<MiniWsConnection, string, Task> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    /// <summary>已接受过的连接数（重连断言用）。</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    public string StreamUrl => $"ws://127.0.0.1:{Port}/v1/stream";

    public IReadOnlyList<MiniWsConnection> Connections
    {
        get { lock (_connections) return [.. _connections]; }
    }

    private MiniWsConnection? FirstOrNull()
    {
        lock (_connections)
            return _connections.Count == 0 ? null : _connections[0];
    }

    /// <summary>等第一条连接建立（握手完成）。</summary>
    public async Task<MiniWsConnection> WaitForConnectionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (FirstOrNull() is { } conn)
                return conn;
            await Task.Delay(20);
        }
        throw new TimeoutException("MiniWsServer: no connection");
    }

    /// <summary>硬断所有连接（RST）。</summary>
    public void AbortAll()
    {
        foreach (var connection in Connections)
            connection.Abort();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }

            var connection = new MiniWsConnection(client);
            lock (_connections)
                _connections.Add(connection);
            Interlocked.Increment(ref _connectionCount);
            _ = Task.Run(() => PumpAsync(connection));
        }
    }

    private async Task PumpAsync(MiniWsConnection connection)
    {
        try
        {
            await connection.HandshakeAsync(_cts.Token);
            while (!_cts.IsCancellationRequested)
            {
                if (await connection.ReadTextAsync(_cts.Token) is not { } text)
                    break;
                await _handler(connection, text);
            }
        }
        catch
        {
            // 连接断开/取消：测试自行断言
        }
        finally
        {
            connection.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        AbortAll();
        _cts.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch { /* 已停止 */ }
        _cts.Dispose();
    }
}

/// <summary>MiniWsServer 上的一条连接。</summary>
internal sealed class MiniWsConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    public MiniWsConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>对端已断（正常或硬断）。</summary>
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task HandshakeAsync(CancellationToken ct)
    {
        var header = await ReadHttpHeaderAsync(ct);
        var keyLine = header.Split("\r\n").First(l => l.StartsWith("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase));
        var key = keyLine[(keyLine.IndexOf(':') + 1)..].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(key + MiniWsServer.WebSocketGuid)));
        var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
            + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await _stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<string> ReadHttpHeaderAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await _stream.ReadAsync(one, ct);
            if (read == 0)
                throw new IOException("connection closed during handshake");
            sb.Append((char)one[0]);
        }
        return sb.ToString();
    }

    /// <summary>读一条文本帧；返回 null 表示对端关闭。</summary>
    public async Task<string?> ReadTextAsync(CancellationToken ct)
    {
        var head = await ReadExactlyAsync(2, ct);
        if (head is null)
            return null;

        var opcode = head[0] & 0x0f;
        var masked = (head[1] & 0x80) != 0;
        long length = head[1] & 0x7f;
        if (length == 126)
        {
            var extended = await ReadExactlyAsync(2, ct);
            if (extended is null) return null;
            length = (extended[0] << 8) | extended[1];
        }
        else if (length == 127)
        {
            var extended = await ReadExactlyAsync(8, ct);
            if (extended is null) return null;
            length = 0;
            foreach (var b in extended)
                length = (length << 8) | b;
        }

        var mask = masked ? await ReadExactlyAsync(4, ct) : null;
        var payload = await ReadExactlyAsync((int)length, ct);
        if (payload is null)
            return null;
        if (mask is not null)
            for (var i = 0; i < payload.Length; i++)
                payload[i] ^= mask[i % 4];

        return opcode switch
        {
            0x8 => null,                                   // close
            0x1 => Encoding.UTF8.GetString(payload),       // text
            0x9 => await ReadTextAsync(ct),                // ping：忽略并继续
            0xA => await ReadTextAsync(ct),                // pong：忽略并继续
            _ => await ReadTextAsync(ct),                  // binary 等：忽略
        };
    }

    private async Task<byte[]?> ReadExactlyAsync(int count, CancellationToken ct)
    {
        if (count == 0)
            return [];
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
                return null;
            offset += read;
        }
        return buffer;
    }

    /// <summary>发一条文本帧（服务端→客户端不掩码）。</summary>
    public async Task SendTextAsync(string text, CancellationToken ct = default)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        using var frame = new MemoryStream();
        frame.WriteByte(0x81);
        if (payload.Length < 126)
        {
            frame.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)(payload.Length & 0xff));
        }
        else
        {
            frame.WriteByte(127);
            for (var shift = 56; shift >= 0; shift -= 8)
                frame.WriteByte((byte)((long)payload.Length >> shift));
        }
        frame.Write(payload);
        await _stream.WriteAsync(frame.ToArray(), ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>发一个 Close 帧（对端将看到"正常关闭"）。</summary>
    public async Task SendCloseAsync(CancellationToken ct = default)
    {
        await _stream.WriteAsync(new byte[] { 0x88, 0x00 }, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>硬断：Linger=0 触发 RST，使对端 socket 进入 Aborted（而非收到 Close 帧）。</summary>
    public void Abort()
    {
        try
        {
            _client.Client.LingerState = new LingerOption(enable: true, seconds: 0);
            _client.Client.Close(0);
        }
        catch
        {
            // 已关闭
        }
        Closed.TrySetResult();
    }

    public void Dispose()
    {
        try
        {
            _stream.Dispose();
            _client.Dispose();
        }
        catch
        {
            // 已释放
        }
        Closed.TrySetResult();
    }
}
