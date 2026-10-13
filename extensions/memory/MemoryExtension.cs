using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Abstractions;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace OpenLuo.Extensions.Memory;

public sealed class MemoryExtension : IAgentExtension
{
    private readonly IMemoryRecallService _recall;
    private readonly IMemoryWriteService _write;

    public MemoryExtension(IMemoryRecallService recall, IMemoryWriteService write)
    {
        _recall = recall;
        _write = write;
    }

    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(MemoryDescriptors.Search, new MemorySearchInvoker(_recall));
        builder.AddCapability(MemoryDescriptors.Write, new MemoryWriteInvoker(_write));
        builder.AddContextContributor<MemoryBaselineContributor>();
    }
}

internal static class MemoryDescriptors
{
    public static CapabilityDescriptor Search => new()
    {
        CanonicalId = "search", DisplayName = "Memory search",
        Summary = "Search long-term memories by meaning or keywords.",
        Usage = "Use when a past fact or event is needed.", Kind = CapabilityKind.Builtin,
        ProviderId = "memory", SideEffect = SideEffectClass.ReadOnly,
        Completion = CompletionPolicy.Continue,
        InputSchema = new { type = "object" }
    };

    public static CapabilityDescriptor Write => new()
    {
        CanonicalId = "write", DisplayName = "Memory write",
        Summary = "Store a durable memory from the current conversation.",
        Usage = "Use for stable user preferences, facts, or important events.", Kind = CapabilityKind.Builtin,
        ProviderId = "memory", SideEffect = SideEffectClass.Mutation,
        Completion = CompletionPolicy.Continue,
        Idempotency = IdempotencyKind.NonIdempotent, InputSchema = new { type = "object" }
    };
}

internal sealed class MemorySearchInvoker : ICapabilityInvoker
{
    private readonly IMemoryRecallService _service;
    public MemorySearchInvoker(IMemoryRecallService service) => _service = service;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var query = call.Options.TryGetValue("query", out var optionQuery) ? optionQuery : string.Join(" ", call.Args);
        var result = await _service.RecallAsync(new SemanticRecallQuery
        {
            GameId = context.GameId, CharacterId = context.SubjectId, SearchText = query,
            // 用户维度来自逐调用下传的执行上下文；平台没给 userId 时为 null → 检索不过滤，行为同改造前。
            UserId = context.UserId,
            TopK = ParsePositiveInt(call.Options, "limit", 5), Scopes = [MemoryScope.CharacterPrivate, MemoryScope.Shared],
            Reason = "agent capability memory:search"
        }, ct);
        var text = string.IsNullOrWhiteSpace(result.Summary)
            ? string.Join("\n", result.Records.Select(r => r.Summary))
            : result.Summary;
        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = result.Success,
            Status = result.Success ? CapabilityStatus.Ok : CapabilityStatus.Failed,
            Text = text, Error = result.Success ? null : "memory recall unavailable"
        };
    }

    private static int ParsePositiveInt(IReadOnlyDictionary<string, string> options, string key, int fallback) =>
        options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed > 0 ? Math.Min(parsed, 50) : fallback;
}

internal sealed class MemoryWriteInvoker : ICapabilityInvoker
{
    private readonly IMemoryWriteService _service;
    public MemoryWriteInvoker(IMemoryWriteService service) => _service = service;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var content = call.Options.TryGetValue("content", out var optionContent) ? optionContent : string.Join(" ", call.Args);
        if (string.IsNullOrWhiteSpace(content))
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = "content is required" };
        var result = await _service.WriteAsync(new MemoryWriteInput
        {
            GameId = context.GameId, CharacterId = context.SubjectId, RawContent = content,
            // 落库归属：有 userId 时这条记忆只对同一用户可见；没有则不分区（老语义）。
            UserId = context.UserId,
            Source = "agent capability memory:write", Importance = ParseImportance(call.Options)
        }, ct);
        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = result.Success,
            Status = result.Success ? CapabilityStatus.Ok : CapabilityStatus.Failed,
            Text = result.Success ? $"memory stored: {result.MemoryId}" : null,
            Error = result.Success ? null : "memory write failed"
        };
    }

    private static float ParseImportance(IReadOnlyDictionary<string, string> options) =>
        options.TryGetValue("importance", out var value) && float.TryParse(value, out var parsed) ? Math.Clamp(parsed, 0f, 1f) : 0.5f;
}

public sealed class MemoryBaselineContributor : IContextContributor
{
    private readonly IMemoryRecallService _service;
    public MemoryBaselineContributor(IMemoryRecallService service) => _service = service;
    public string Id => "memory:baseline";

    /// <summary>长期记忆：属于「用户↔角色」，跨会话（D45）。</summary>
    public ContextScope Scope => ContextScope.User;

    public async Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserInput))
                return Ok([]);
            var result = await _service.RecallAsync(new SemanticRecallQuery
            {
                GameId = request.SessionId, CharacterId = request.SubjectId, SearchText = request.UserInput,
                // 用域键（唯一形状定义处）判空：取不到 User 域键说明平台没给 userId，
                // 此时 UserId 留空 → 召回退回 (会话, 角色) 分区、含未分区记忆，与改造前一致。
                UserId = request.DomainKey(ContextScope.User) is null ? null : request.UserId,
                TopK = 3, Scopes = [MemoryScope.CharacterPrivate, MemoryScope.Shared], Reason = "memory baseline context"
            }, ct);
            var text = string.IsNullOrWhiteSpace(result.Summary) ? string.Join("\n", result.Records.Select(r => r.Summary)) : result.Summary;
            return Ok(string.IsNullOrWhiteSpace(text) ? [] : [new ContextContribution
            {
                Id = "memory-baseline", ContributorId = Id, Region = ContextRegion.LongTermMemory,
                Content = text, Priority = 40
            }]);
        }
        catch (Exception ex)
        {
            return new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Unavailable, Reason = ex.Message, Retryable = true }
            };
        }
    }

    private ContextContributionResult Ok(IReadOnlyList<ContextContribution> contributions) => new()
    {
        State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok }, Contributions = contributions
    };
}
