using openLuo.AgentContext.Core;
using openLuo.Capabilities.Core;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认 Skill 服务（D12/D31/D33）：**不可变的文档库**（启动期装载，重启即更新）。
/// "已加载正文"的记账不在这里——它按对话维度保存在 <c>ILoadedSkillSet</c>，
/// 并由 <c>LoadedSkillsContributor</c> 在每个请求上下文里以引用展开的方式注入。
/// </summary>
public sealed class DefaultSkillService : ISkillService
{
    private readonly IReadOnlyDictionary<string, SkillDocument> _documents;

    public DefaultSkillService(IEnumerable<SkillDocument> documents)
    {
        _documents = documents.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Task<SkillSummary?> GetSummaryAsync(string skillId, CancellationToken ct = default) =>
        Task.FromResult(_documents.TryGetValue(skillId, out var doc)
            ? new SkillSummary
            {
                Id = doc.Id,
                Title = doc.Title,
                Summary = doc.Summary,
                WhenToUse = doc.WhenToUse
            }
            : null);

    public Task<SkillDocument?> LoadFullAsync(string skillId, CancellationToken ct = default) =>
        Task.FromResult(_documents.TryGetValue(skillId, out var doc) ? doc : null);

    public IReadOnlyList<SkillSummary> ListSummaries(CancellationToken ct = default) =>
        _documents.Values
            .Select(d => new SkillSummary
            {
                Id = d.Id,
                Title = d.Title,
                Summary = d.Summary,
                WhenToUse = d.WhenToUse
            })
            .ToList();
}
