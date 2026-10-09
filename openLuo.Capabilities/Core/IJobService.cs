using System.Text.Json.Nodes;

namespace openLuo.Capabilities.Core;

/// <summary>作业状态名（与协议 `JobStatuses` 取值一致）。</summary>
public static class JobStatusNames
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>作业快照（协议 `GET /v1/jobs/{id}`）。</summary>
public sealed record JobInfo(
    string Id,
    string Kind,
    string Status,
    double Progress,
    string? Message,
    string? SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>作业事件（Hub 转发为 `job.*` 事件）。<see cref="ErrorMsg"/> 仅失败时有值。</summary>
public sealed record JobEvent(string Kind, JobInfo Job, string? ErrorMsg = null);

/// <summary>作业执行上下文：handler 据此读负载并上报进度。</summary>
public sealed class JobContext
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public JsonNode? Payload { get; init; }
    public string? SessionId { get; init; }

    /// <summary>上报进度（0..1）与可选消息。</summary>
    public Action<double, string?>? Progress { get; init; }
}

/// <summary>作业处理器（按 kind 注册）。CG 生成 / 批量 TTS 等长任务实现本接口。</summary>
public interface IJobHandler
{
    string Kind { get; }

    Task RunAsync(JobContext context, CancellationToken ct);
}

/// <summary>
/// 长任务服务端口（协议 §5.9）：提交 / 查询 / 取消 + 事件流（受理、进度、完成、失败）。
/// 由宿主实现；内核不感知具体任务类型。
/// </summary>
public interface IJobService
{
    /// <summary>提交作业；无对应 kind 的 handler 时抛 <see cref="InvalidOperationException"/>。</summary>
    Task<JobInfo> SubmitAsync(string kind, JsonNode? payload, string? sessionId = null, CancellationToken ct = default);

    JobInfo? Get(string id);

    IReadOnlyList<JobInfo> List();

    /// <summary>取消进行中的作业；返回是否命中。</summary>
    bool Cancel(string id);

    /// <summary>订阅作业事件（单消费者，供 Hub 转发）。</summary>
    IAsyncEnumerable<JobEvent> WatchAsync(CancellationToken ct = default);
}
