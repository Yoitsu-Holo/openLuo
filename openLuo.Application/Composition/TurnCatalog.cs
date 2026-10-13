using openLuo.AgentContext.Core;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 每轮注入决策上下文的「我能做什么」目录（D30）：技能 / 工作流 / 远程 Agent 的**摘要**。
/// 只给摘要不是省事，而是设计：正文/编排按需拉取（`core:load_skill` / `core:run_workflow`），
/// 目录才能一直小到可以每轮都带上。
/// </summary>
public interface ITurnCatalog
{
    IReadOnlyList<SkillSummary> Skills { get; }
    IReadOnlyList<WorkflowSummary> Workflows { get; }
    IReadOnlyList<RemoteAgentSummary> RemoteAgents { get; }
}

/// <summary>空目录：测试与"未接线"宿主用（行为 = 目录恒空，与接线前一致）。</summary>
public sealed class NullTurnCatalog : ITurnCatalog
{
    public static NullTurnCatalog Instance { get; } = new();

    public IReadOnlyList<SkillSummary> Skills => [];
    public IReadOnlyList<WorkflowSummary> Workflows => [];
    public IReadOnlyList<RemoteAgentSummary> RemoteAgents => [];
}

/// <summary>
/// 从三处汇总目录：技能来自 <see cref="ISkillService"/>（扩展声明 + 文件加载的技能都在它手里），
/// 工作流来自扩展注册表，远程 Agent 来自 A2A 能力源（连接成功后才有 Agent Card）。
/// 全部**按需读取**（每次取属性都重算），所以扩展在启动后加载完毕再解析也来得及。
/// </summary>
public sealed class RegisteredTurnCatalog(
    ExtensionRegistry registry,
    ISkillService skills,
    IEnumerable<ICapabilitySource> sources) : ITurnCatalog
{
    public IReadOnlyList<SkillSummary> Skills => skills.ListSummaries();

    public IReadOnlyList<WorkflowSummary> Workflows =>
        [.. registry.Workflows.Select(w => new WorkflowSummary { Id = w.Id, Description = w.Description })];

    public IReadOnlyList<RemoteAgentSummary> RemoteAgents =>
    [
        .. sources
            .OfType<openLuo.Capabilities.A2A.A2ACapabilitySource>()
            .Where(source => source.AgentCard is not null)
            .Select(source => new RemoteAgentSummary
            {
                Id = source.ProviderId,
                DisplayName = source.AgentCard!.Name,
                Description = source.AgentCard.Description ?? string.Empty,
            }),
    ];
}
