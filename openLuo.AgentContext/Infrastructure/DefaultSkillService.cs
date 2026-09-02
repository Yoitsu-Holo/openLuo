using openLuo.Capabilities.Core;
using System.Collections.Concurrent;
using openLuo.AgentContext.Core;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认 Skill 服务（D12/D31/D33）。摘要注入目录；完整内容按需加载（会话缓存），
/// 超预算时按相关性淘汰最久未使用项（LRU 简化实现）。
/// </summary>
public sealed class DefaultSkillService : ISkillService
{
    private readonly IReadOnlyDictionary<string, SkillDocument> _documents;
    private readonly ConcurrentDictionary<string, SkillDocument> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxLoaded;

    public DefaultSkillService(IEnumerable<SkillDocument> documents, int maxLoaded = 5)
    {
        _documents = documents.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        _maxLoaded = Math.Max(1, maxLoaded);
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

    public Task<SkillDocument?> LoadFullAsync(string skillId, CancellationToken ct = default)
    {
        if (!_documents.TryGetValue(skillId, out var doc))
            return Task.FromResult<SkillDocument?>(null);

        _loaded[skillId] = doc;

        // 超上限：淘汰最久未使用（简化：移除第一个；真实实现按 LRU/相关性）
        while (_loaded.Count > _maxLoaded)
        {
            var victim = _loaded.Keys.FirstOrDefault(k => !string.Equals(k, skillId, StringComparison.OrdinalIgnoreCase));
            if (victim is null)
                break;
            _loaded.TryRemove(victim, out _);
        }

        return Task.FromResult<SkillDocument?>(doc);
    }

    public Task UnloadAsync(string skillId, CancellationToken ct = default)
    {
        _loaded.TryRemove(skillId, out _);
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> ListLoaded(CancellationToken ct = default) => [.. _loaded.Keys];

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
