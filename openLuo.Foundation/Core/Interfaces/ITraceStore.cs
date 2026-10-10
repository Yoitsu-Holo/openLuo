namespace openLuo.Core.Interfaces;

/// <summary>回合轨迹中的单条事件（形状与 WS 事件一致：decision/tool.call/tool.result/output）。</summary>
public sealed record TraceEventInfo(int Seq, DateTimeOffset Ts, string Type, string? Data);

/// <summary>一个回合的完整轨迹（协议 `GET /v1/traces/{turnId}`）。</summary>
public sealed record TurnTraceInfo(
    string TurnId,
    string SessionId,
    string? Origin,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    bool? Success,
    string? TerminationReason,
    string? FinalText,
    IReadOnlyList<TraceEventInfo> Events);

/// <summary>
/// 回合轨迹存储端口（协议 §5.11 观测/回放）：记录 decision/tool/output 事件序列与结果摘要，
/// 供 `/v1/traces/{turnId}` 回放排查。由宿主实现（复用 hub.db）。
/// </summary>
public interface ITraceStore
{
    void StartTurn(string turnId, string sessionId, string? origin, DateTimeOffset startedAt);

    /// <summary>追加一条事件（<paramref name="data"/> 为该事件的 JSON 载荷）。</summary>
    void AddEvent(string turnId, string type, string? data);

    void CompleteTurn(string turnId, bool success, string? terminationReason, string? finalText);

    TurnTraceInfo? Get(string turnId);
}
