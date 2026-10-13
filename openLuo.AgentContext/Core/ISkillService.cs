using openLuo.Capabilities.Core;

namespace openLuo.AgentContext.Core;


/// <summary>Skill 摘要（注入目录，D30）。</summary>
public sealed class SkillDocument
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string WhenToUse { get; init; } = string.Empty;
    public string FullContent { get; init; } = string.Empty;
    public IReadOnlyList<string> RelatedTools { get; init; } = [];
    public IReadOnlyList<string> RelatedWorkflows { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
}

/// <summary>
/// Skill 服务（D12/D31/D33）：**文档库**。摘要注入每轮目录；正文由 <c>core:load_skill</c> 标记"已加载"后，
/// 在后续请求里以**引用展开**的形式注入（见 docs/architecture/skills-workflows.md §4）。
/// <para>
/// 这里**不记录**"谁加载了什么"：那是按对话维度的状态，属于 <c>ILoadedSkillSet</c>。
/// （早期实现把已加载集合放在本服务里，导致它是单例级、跨会话串味，且没有任何消费者。）
/// </para>
/// </summary>
public interface ISkillService
{
    Task<SkillSummary?> GetSummaryAsync(string skillId, CancellationToken ct = default);
    Task<SkillDocument?> LoadFullAsync(string skillId, CancellationToken ct = default);
    IReadOnlyList<SkillSummary> ListSummaries(CancellationToken ct = default);
}
