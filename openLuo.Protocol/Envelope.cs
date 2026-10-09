using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace openLuo.Protocol;

/// <summary>
/// 统一协议信封：HTTP body 与 WebSocket 帧**共用同一结构**。
/// <see cref="Data"/> 为类型特定载荷（弱类型 JSON 树）；类型化便捷见
/// <see cref="EnvelopeFactory"/> 与 <see cref="EnvelopeExtensions"/>。
/// 响应状态用 <see cref="ErrorCode"/>（int）+ <see cref="ErrorMsg"/> 表达：
/// <see cref="ErrorCodes.Success"/>(1000) 表示成功，其余为错误。
/// </summary>
public sealed class Envelope
{
    /// <summary>协议 major 版本（见 <see cref="ProtocolInfo.MajorVersion"/>）。</summary>
    public int V { get; init; } = ProtocolInfo.MajorVersion;

    /// <summary>本条消息 id（ULID，见 <see cref="ProtocolIds.NewUlid"/>）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>消息类型（点分命名空间；命令见 <see cref="MessageTypes"/>，事件见 <see cref="EventTypes"/>）。</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>发出时间（RFC3339 UTC）。</summary>
    public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>会话域消息携带。</summary>
    public string? SessionId { get; init; }

    /// <summary>链路追踪 id。</summary>
    public string? TraceId { get; init; }

    /// <summary>响应/结果指向的请求 id。</summary>
    public string? ReplyTo { get; init; }

    /// <summary>定向投递目标客户端（多客户端场景）；null = 广播给订阅者。</summary>
    public string? TargetClientId { get; init; }

    /// <summary>类型特定载荷。</summary>
    public JsonNode? Data { get; init; }

    /// <summary>错误码（见 <see cref="ErrorCodes"/>）；成功为 1000。</summary>
    public int ErrorCode { get; init; } = ErrorCodes.Success;

    /// <summary>错误消息（人读）；成功时为空串。</summary>
    public string ErrorMsg { get; init; } = string.Empty;

    /// <summary>是否为错误响应。</summary>
    public bool IsError => ErrorCode != ErrorCodes.Success;
}

/// <summary>协议 JSON 序列化约定：camelCase 属性名、枚举 camelCase 字符串、null 忽略。</summary>
public static class ProtocolJson
{
    /// <summary>全局共享的序列化选项（线程安全，只读使用）。</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>
/// 协议 id 生成。ULID 风格：48-bit 毫秒时间戳 + 80-bit 随机，
/// Crockford Base32 编码为 26 字符，字典序 ≈ 时间序。
/// </summary>
public static class ProtocolIds
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewUlid()
    {
        Span<byte> bytes = stackalloc byte[16];
        long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        Random.Shared.NextBytes(bytes[6..]);

        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        Span<char> chars = stackalloc char[26];
        for (int i = 25; i >= 0; i--)
        {
            chars[i] = Crockford[(int)(value & 31)];
            value >>= 5;
        }
        return new string(chars);
    }
}

/// <summary>信封构造工厂（自动生成 id、类型化 data → JSON 树）。</summary>
public static class EnvelopeFactory
{
    /// <summary>构造一条成功消息（命令或事件）。</summary>
    public static Envelope Create<TData>(
        string type,
        TData? data,
        string? sessionId = null,
        string? replyTo = null,
        string? traceId = null) => new()
    {
        Id = ProtocolIds.NewUlid(),
        Type = type,
        SessionId = sessionId,
        ReplyTo = replyTo,
        TraceId = traceId,
        Data = data is null ? null : JsonSerializer.SerializeToNode(data, ProtocolJson.Options),
    };

    /// <summary>构造一条错误消息（errorCode + errorMsg）。</summary>
    public static Envelope CreateError(
        string type,
        int errorCode,
        string errorMsg,
        string? replyTo = null,
        string? sessionId = null,
        string? traceId = null) => new()
    {
        Id = ProtocolIds.NewUlid(),
        Type = type,
        SessionId = sessionId,
        ReplyTo = replyTo,
        TraceId = traceId,
        ErrorCode = errorCode,
        ErrorMsg = errorMsg,
    };
}

/// <summary>信封读取扩展。</summary>
public static class EnvelopeExtensions
{
    /// <summary>把 <see cref="Envelope.Data"/> 反序列化为强类型载荷。</summary>
    public static T? DataAs<T>(this Envelope envelope) =>
        envelope.Data is null ? default : envelope.Data.Deserialize<T>(ProtocolJson.Options);
}
