using openLuo.AgentContext.Core;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Composition;
using Xunit;

namespace openLuo.Tests.Skills;

/// <summary>
/// 技能 / 工作流的"目录 + 按需使用"（D12/D13/D30）。这套机制早就写好却从未接线，
/// 这里锁住接线后的三条链：目录能列出来、正文能按需加载、工作流能黑盒执行。
/// </summary>
public sealed class SkillWorkflowCapabilityTests
{
    private static readonly SkillDocument Comfort = new()
    {
        Id = "companion:comfort",
        Title = "安慰",
        Summary = "用户情绪低落时的陪伴做法",
        WhenToUse = "用户表达难过、受挫、需要被理解时",
        FullContent = "先接住情绪，再问发生了什么；不要急着给建议。",
        Constraints = ["不要提及自己是 AI", "不要说教"],
    };

    private static SkillDocument[] Documents => [Comfort];

    private static CapabilityCall Call(params (string Key, string Value)[] options) => new()
    {
        InvocationId = "inv-1",
        CanonicalId = SkillCapabilitySource.CanonicalId,
        Options = options.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
    };

    private static CapabilityExecutionContext Context() => new() { SessionId = "s", SubjectId = "builtin-rin", TurnId = "t" };

    [Fact]
    public async Task LoadSkill_RegistersReferenceAndAcksWithoutBody()
    {
        // 引用方案（D30）：能力只登记"本对话要用这个技能"，**不回填正文**——
        // 正文由 LoadedSkillsContributor 在每个请求里展开（见该类测试）。
        var loaded = new InMemoryLoadedSkillSet();
        var source = new SkillCapabilitySource(new DefaultSkillService(Documents), loaded);
        var result = await source.CreateInvoker().InvokeAsync(Call(("id", "companion:comfort")), Context());

        Assert.True(result.Success);
        Assert.Contains("comfort", result.Text);                 // 确认信息里带 id
        Assert.DoesNotContain("先接住情绪", result.Text);          // 正文不在这里
        Assert.Equal(["companion:comfort"], loaded.Get("s"));    // 但引用被登记了
    }

    [Fact]
    public async Task LoadSkill_IsIdempotentPerConversation()
    {
        var loaded = new InMemoryLoadedSkillSet();
        var source = new SkillCapabilitySource(new DefaultSkillService(Documents), loaded);
        var invoker = source.CreateInvoker();

        await invoker.InvokeAsync(Call(("id", "companion:comfort")), Context());
        await invoker.InvokeAsync(Call(("id", "companion:comfort")), Context());

        Assert.Equal(["companion:comfort"], loaded.Get("s"));   // 重复加载不会重复登记
    }

    [Fact]
    public async Task UnloadSkill_RemovesReferenceAndIsIdempotent()
    {
        var loaded = new InMemoryLoadedSkillSet();
        await new SkillCapabilitySource(new DefaultSkillService(Documents), loaded)
            .CreateInvoker().InvokeAsync(Call(("id", "companion:comfort")), Context());

        var unload = new UnloadSkillCapabilitySource(loaded);
        var first = await unload.CreateInvoker().InvokeAsync(Call(("id", "companion:comfort")), Context());
        var second = await unload.CreateInvoker().InvokeAsync(Call(("id", "companion:comfort")), Context());

        Assert.True(first.Success);
        Assert.True(second.Success);                             // 幂等：没加载过也算成功，模型不必猜状态
        Assert.Empty(loaded.Get("s"));
    }

    [Fact]
    public async Task LoadSkill_UnknownOrMissingId_IsRejectedNotFailed()
    {
        var source = new SkillCapabilitySource(new DefaultSkillService(Documents), new InMemoryLoadedSkillSet());
        var invoker = source.CreateInvoker();

        var unknown = await invoker.InvokeAsync(Call(("id", "nope")), Context());
        var missing = await invoker.InvokeAsync(new CapabilityCall { InvocationId = "inv-2", CanonicalId = source.ListCapabilities()[0].CanonicalId }, Context());

        // 参数/引用类错误用 Rejected（确定性错误，不该触发派发器重试）
        Assert.Equal(CapabilityStatus.Rejected, unknown.Status);
        Assert.Equal(CapabilityStatus.Rejected, missing.Status);
    }

    [Fact]
    public void SkillSource_AdvertisesCoreLoadSkillDescriptor()
    {
        var descriptor = Assert.Single(
            new SkillCapabilitySource(new DefaultSkillService(Documents), new InMemoryLoadedSkillSet()).ListCapabilities());

        Assert.Equal("core:load_skill", descriptor.CanonicalId);
        Assert.Equal("core", descriptor.ProviderId);
        Assert.Equal(SideEffectClass.Pure, descriptor.SideEffect);
    }

    [Fact]
    public async Task RunWorkflow_PassesIdAndFlattensRemainingOptionsAsInput()
    {
        var runner = new RecordingRunner();
        var source = new WorkflowCapabilitySource(runner);

        var result = await source.CreateInvoker().InvokeAsync(
            Call(("id", "world:gift"), ("target", "rin"), ("amount", "2")), Context());

        Assert.True(result.Success);
        Assert.Equal("world:gift", runner.LastRequest!.WorkflowId);
        // 嵌套对象过不了 wire（只承载标量），所以输入走扁平命名参数
        Assert.Equal("rin", runner.LastRequest.Input["target"]);
        Assert.Equal("2", runner.LastRequest.Input["amount"]);
        Assert.DoesNotContain("id", runner.LastRequest.Input.Keys);
        Assert.Contains("world:gift", result.Text);
        Assert.Contains("step-2", result.Text);   // 只回填"走到哪一步"，不外泄节点细节
    }

    [Fact]
    public async Task RunWorkflow_FailureIsReportedAsFailed()
    {
        var runner = new RecordingRunner { Fail = "handler exploded" };
        var source = new WorkflowCapabilitySource(runner);

        var result = await source.CreateInvoker().InvokeAsync(Call(("id", "world:gift")), Context());

        Assert.False(result.Success);
        Assert.Equal(CapabilityStatus.Failed, result.Status);
        Assert.Contains("handler exploded", result.Error);
    }

    [Fact]
    public async Task RunWorkflow_MissingId_IsRejected()
    {
        var source = new WorkflowCapabilitySource(new RecordingRunner());

        var result = await source.CreateInvoker().InvokeAsync(
            new CapabilityCall { InvocationId = "inv-3", CanonicalId = WorkflowCapabilitySource.CanonicalId }, Context());

        Assert.Equal(CapabilityStatus.Rejected, result.Status);
    }

    [Fact]
    public void WorkflowSource_IsNotDeclaredParallelSafe()
    {
        // 工作流可能有共享资源与副作用 → 保守声明不可并行，并把副作用类别暴露给策略层
        var descriptor = Assert.Single(new WorkflowCapabilitySource(new RecordingRunner()).ListCapabilities());

        Assert.Equal("core:run_workflow", descriptor.CanonicalId);
        Assert.False(descriptor.ParallelSafe);
        Assert.Equal(SideEffectClass.Mutation, descriptor.SideEffect);
    }

    private sealed class RecordingRunner : IWorkflowRunner
    {
        public WorkflowRunRequest? LastRequest { get; private set; }
        public string? Fail { get; init; }

        /// <summary>本测试只关心"收到什么请求"，注册定义不参与断言。</summary>
        public void Register(WorkflowDefinition definition)
        {
        }

        public Task<WorkflowRunResult> RunAsync(WorkflowRunRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(Fail is null
                ? new WorkflowRunResult
                {
                    Success = true, Steps = ["step-1", "step-2"], TerminalNodeId = "step-2",
                    Outputs = new Dictionary<string, object?> { ["ok"] = true },
                }
                : new WorkflowRunResult { Success = false, Error = Fail });
        }
    }
}
