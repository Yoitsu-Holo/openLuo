using openLuo.AgentContext.Core;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 技能能力的来源（D12/D30）：`core:load_skill`。
/// <para>
/// 设计意图（契约注释里早就写明、此前从未接线）：每轮上下文只注入技能**摘要目录**（id/title/whenToUse），
/// 正文按需加载——目录小、不占预算，模型要照做时才把完整做法拉进对话。
/// 加载经 <see cref="ISkillService"/> 的会话缓存（LRU 淘汰），所以反复加载同一技能不会膨胀上下文。
/// </para>
/// </summary>
public sealed class SkillCapabilitySource(ISkillService skills, ILoadedSkillSet loaded) : ICapabilitySource
{
    /// <summary>能力 id（`core:` 前缀为内核保留，扩展不得占用）。</summary>
    public const string CanonicalId = "core:load_skill";

    public string ProviderId => "core";

    public IReadOnlyList<CapabilityDescriptor> ListCapabilities() =>
    [
        new CapabilityDescriptor
        {
            CanonicalId = CanonicalId,
            DisplayName = "加载技能正文",
            Summary = "按 id 加载某个技能（只登记引用，正文随后自动出现在 [LoadedSkills] 块里）",
            Usage = "当 [Skills] 目录里的 whenToUse 命中当前请求时调用；**不要重复加载同一技能**，用完可调 core:unload_skill 释放",
            Kind = CapabilityKind.Builtin,
            ProviderId = ProviderId,
            SideEffect = SideEffectClass.Pure,
            Completion = CompletionPolicy.Continue,
            ParallelSafe = true,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "技能 id（见 [Skills] 目录）" },
                },
                required = new[] { "id" },
            },
        },
    ];

    public ICapabilityInvoker CreateInvoker() => new LoadSkillInvoker(skills, loaded);
}

internal sealed class LoadSkillInvoker(ISkillService skills, ILoadedSkillSet loaded) : ICapabilityInvoker
{
    public async Task<CapabilityResult> InvokeAsync(
        CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var id = call.Options.TryGetValue("id", out var value) ? value : call.Args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            return Reject(call, "id is required");

        // 先确认技能真的存在（写错 id 要立刻回 Rejected 让模型纠正），但**不回填正文**：
        // 正文是"引用"，由 LoadedSkillsContributor 在每个请求里从技能库展开——
        // 这样上下文里永远是当前版本，且不把正文当成对话内容。
        var document = await skills.LoadFullAsync(id, ct);
        if (document is null)
            return Reject(call, $"unknown skill: {id}");

        var set = loaded.MarkLoaded(ConversationKeys.For(context), id);
        var text = $"已加载技能「{document.Title}」（{document.Id}）。正文会随后续请求自动注入，无需重复加载。";
        if (set.Count >= loaded.Capacity && !set.Contains(id, StringComparer.OrdinalIgnoreCase))
            text += $" 注意：本对话最多同时加载 {loaded.Capacity} 个技能，较久未用的会被自动淘汰。";

        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok, Text = text,
        };
    }

    private static CapabilityResult Reject(CapabilityCall call, string error) => new()
    {
        InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = error,
    };
}

/// <summary>
/// 工作流能力的来源（D13）：`core:run_workflow`。
/// <para>
/// 工作流是**黑盒能力**（契约注释原文）：模型只给 workflowId + 输入，内部节点由确定性 runner 执行，
/// 模型看不到也控制不了中间步骤。输入走**扁平命名参数**（模型的 `args`/`options` 只承载标量，
/// 嵌套对象在 wire 侧会被丢弃）——handler 需要结构化输入时自行把字符串当 JSON 解析。
/// </para>
/// </summary>
public sealed class WorkflowCapabilitySource(IWorkflowRunner runner) : ICapabilitySource
{
    /// <summary>能力 id。</summary>
    public const string CanonicalId = "core:run_workflow";

    /// <summary>输入参数里表示"工作流 id"的键名，其余键全部作为工作流输入。</summary>
    public const string WorkflowIdOption = "id";

    public string ProviderId => "core";

    public IReadOnlyList<CapabilityDescriptor> ListCapabilities() =>
    [
        new CapabilityDescriptor
        {
            CanonicalId = CanonicalId,
            DisplayName = "执行工作流",
            Summary = "按 [Workflows] 目录里的 id 触发一个多步编排（内部步骤对模型不可见）",
            Usage = "当 [Workflows] 目录里的 description 命中当前请求时调用；除 id 外的参数会作为工作流输入",
            Kind = CapabilityKind.Builtin,
            ProviderId = ProviderId,
            // 工作流可能产生副作用（送礼、发消息等）→ 声明为 Mutation，让策略层有据可依
            SideEffect = SideEffectClass.Mutation,
            Completion = CompletionPolicy.Continue,
            // 同一个工作流可能有共享资源；交给 runner/策略判定，这里保守声明不可并行
            ParallelSafe = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "工作流 id（见 [Workflows] 目录）" },
                },
                required = new[] { "id" },
            },
        },
    ];

    public ICapabilityInvoker CreateInvoker() => new RunWorkflowInvoker(runner);
}

internal sealed class RunWorkflowInvoker(IWorkflowRunner runner) : ICapabilityInvoker
{
    public async Task<CapabilityResult> InvokeAsync(
        CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var workflowId = call.Options.TryGetValue(WorkflowCapabilitySource.WorkflowIdOption, out var value)
            ? value
            : call.Args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(workflowId))
            return Reject(call, "id is required");

        // 除 id 外的参数即工作流输入（扁平字符串；嵌套结构在 wire 侧会被丢弃，别依赖）
        var input = call.Options
            .Where(pair => !string.Equals(pair.Key, WorkflowCapabilitySource.WorkflowIdOption, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);

        var result = await runner.RunAsync(new WorkflowRunRequest
        {
            WorkflowId = workflowId,
            SubjectId = context.SubjectId,
            Input = input,
            IdempotencyKey = call.IdempotencyKey,
            CancellationToken = ct,
        }, ct);

        if (!result.Success)
            return new CapabilityResult
            {
                InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Failed,
                Error = result.Error ?? $"workflow failed: {workflowId}",
            };

        // 结果只回填"走到哪一步 + 各步产出"，内部节点细节不外泄（黑盒语义）
        var text = result.Steps.Count == 0
            ? $"workflow {workflowId} completed"
            : $"workflow {workflowId} completed: {string.Join(" → ", result.Steps)}";
        if (result.Outputs.Count > 0)
            text += $"\noutputs: {string.Join("; ", result.Outputs.Select(o => $"{o.Key}={o.Value}"))}";

        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok, Text = text,
        };
    }

    private static CapabilityResult Reject(CapabilityCall call, string error) => new()
    {
        InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected, Error = error,
    };
}

/// <summary>
/// 卸载技能的能力（D30）：把 id 从"本对话已加载集"里移除，正文随即从后续请求的 [LoadedSkills] 块消失。
/// 存在的意义是把上下文的**预算控制权交给模型**——用完的技能不该一直占着系统块预算。
/// </summary>
public sealed class UnloadSkillCapabilitySource(ILoadedSkillSet loaded) : ICapabilitySource
{
    /// <summary>能力 id。</summary>
    public const string CanonicalId = "core:unload_skill";

    public string ProviderId => "core";

    public IReadOnlyList<CapabilityDescriptor> ListCapabilities() =>
    [
        new CapabilityDescriptor
        {
            CanonicalId = CanonicalId,
            DisplayName = "卸载技能",
            Summary = "把已加载技能的正文从后续请求里移除（释放上下文预算）",
            Usage = "当一个已加载技能不再与当前任务相关时调用",
            Kind = CapabilityKind.Builtin,
            ProviderId = ProviderId,
            SideEffect = SideEffectClass.Pure,
            Completion = CompletionPolicy.Continue,
            ParallelSafe = true,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "技能 id（见 [Skills] 目录）" },
                },
                required = new[] { "id" },
            },
        },
    ];

    public ICapabilityInvoker CreateInvoker() => new UnloadSkillInvoker(loaded);
}

internal sealed class UnloadSkillInvoker(ILoadedSkillSet loaded) : ICapabilityInvoker
{
    public Task<CapabilityResult> InvokeAsync(
        CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var id = call.Options.TryGetValue("id", out var value) ? value : call.Args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Rejected,
                Error = "id is required",
            });

        var removed = loaded.Unload(ConversationKeys.For(context), id);
        return Task.FromResult(new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok,
            // 没加载过也算成功（幂等）：让模型不必猜当前状态
            Text = removed ? $"已卸载技能 {id}。" : $"技能 {id} 本就未加载（无需处理）。",
        });
    }
}
