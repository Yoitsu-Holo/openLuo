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
/// Skill 服务（D12/D31/D33）。摘要注入每轮目录；完整内容按需加载（core:load_skill），
/// 会话缓存 + 相关性动态淘汰。
/// </summary>
public interface ISkillService
{
    Task<SkillSummary?> GetSummaryAsync(string skillId, CancellationToken ct = default);
    Task<SkillDocument?> LoadFullAsync(string skillId, CancellationToken ct = default);
    Task UnloadAsync(string skillId, CancellationToken ct = default);
    IReadOnlyList<string> ListLoaded(CancellationToken ct = default);
    IReadOnlyList<SkillSummary> ListSummaries(CancellationToken ct = default);
}
