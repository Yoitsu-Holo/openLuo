using System.Text.Json.Nodes;

namespace openLuo.Capabilities.Core;

/// <summary>调度条目（协议 `GET /v1/schedules`）。</summary>
public sealed record ScheduleInfo(
    string Id,
    string SessionId,
    string Kind,
    DateTimeOffset? At,
    string? Cron,
    bool Enabled,
    DateTimeOffset? NextRunAt,
    JsonNode? Payload);

/// <summary>到期触发（Hub 据此发起主动回合）。</summary>
public sealed record ScheduleDue(string Id, string SessionId, string Kind, JsonNode? Payload);

/// <summary>
/// 调度服务端口（协议 §5.8）：注册定时 / 一次性触发；到期经 <see cref="WatchAsync"/> 通报，
/// 由 Hub 发起主动回合（`turn.started{origin:"scheduled"}`）。
/// </summary>
public interface ISchedulerService
{
    /// <summary>新增调度；不支持的 Cron 抛 <see cref="InvalidOperationException"/>。</summary>
    ScheduleInfo Add(string sessionId, string kind, DateTimeOffset? at, string? cron, JsonNode? payload);

    ScheduleInfo? Get(string id);

    IReadOnlyList<ScheduleInfo> List();

    bool Remove(string id);

    /// <summary>订阅到期事件（单消费者，供 Hub 转发）。</summary>
    IAsyncEnumerable<ScheduleDue> WatchAsync(CancellationToken ct = default);
}
