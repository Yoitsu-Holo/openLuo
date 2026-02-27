using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 能力来源（D14）：提供一组能力描述。Builtin / MCP / Workflow / RemoteAgent 各实现一个 source。
/// </summary>
public interface ICapabilitySource
{
    /// <summary>提供方标识："builtin" / "mcp:image-server" / "world" / "core"。</summary>
    string ProviderId { get; }

    IReadOnlyList<CapabilityDescriptor> ListCapabilities();
}

/// <summary>
/// 能力执行器（D7）：执行单次调用并返回结构化结果。
/// 扩展按 CapabilityKind 或 canonical id 前缀分发到各自实现。
/// </summary>
public interface ICapabilityInvoker
{
    Task<CapabilityResult> InvokeAsync(
        CapabilityCall call,
        CapabilityExecutionContext context,
        CancellationToken ct = default);
}

/// <summary>前线快照读取面（D7）：兄弟节点只读同一快照，互不影响。</summary>
public interface IReadOnlySnapshot
{
    StateSnapshot? Get(string subjectId);
    object? GetValue(string subjectId, string resourcePath);
    long GetVersion(string subjectId);
}

/// <summary>mutation intent 收集器（D9）：能力执行时只提案，不直接写状态。</summary>
public interface IMutationCollector
{
    void Add(MutationIntent intent);
    IReadOnlyList<MutationIntent> Collected { get; }
}

/// <summary>
/// 能力执行上下文。兄弟节点共享同一实例：同一 ReadSnapshot、同一 MutationCollector、同一 OutputQueue。
/// </summary>
public sealed class CapabilityExecutionContext
{
    public string GameId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public long SnapshotVersion { get; init; }
    public string InvocationId { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; init; }
    public CapabilityPermissions Permissions { get; init; } = CapabilityPermissions.AllowAll;
    public IReadOnlySnapshot ReadSnapshot { get; init; } = NullSnapshot.Instance;
    public IMutationCollector MutationCollector { get; init; } = new ListMutationCollector();
    public IOutputQueue OutputQueue { get; init; } = NullOutputQueue.Instance;
    /// <summary>当前回合的系统块（[Region] 内容），如身份/persona/时间，供能力 invoker 消费。</summary>
    public IReadOnlyList<string> SystemBlocks { get; init; } = [];
}

internal sealed class NullSnapshot : IReadOnlySnapshot
{
    public static readonly NullSnapshot Instance = new();
    public StateSnapshot? Get(string subjectId) => null;
    public object? GetValue(string subjectId, string resourcePath) => null;
    public long GetVersion(string subjectId) => 0;
}

internal sealed class ListMutationCollector : IMutationCollector
{
    private readonly List<MutationIntent> _intents = [];
    public void Add(MutationIntent intent) => _intents.Add(intent);
    public IReadOnlyList<MutationIntent> Collected => _intents;
}

internal sealed class NullOutputQueue : IOutputQueue
{
    public static readonly NullOutputQueue Instance = new();
    public ValueTask<long> EnqueueAsync(OutputItem item, CancellationToken ct = default) => new(0);
    public IAsyncEnumerable<OutputItem> ReadAsync(CancellationToken ct = default) => Empty();
    public Task AckAsync(long sequence, CancellationToken ct = default) => Task.CompletedTask;
    public Task FailAsync(long sequence, bool permanent, CancellationToken ct = default) => Task.CompletedTask;
    private static async IAsyncEnumerable<OutputItem> Empty() { await Task.CompletedTask; yield break; }
}
