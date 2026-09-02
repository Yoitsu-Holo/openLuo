using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 统一能力上下文注入（全局管线的一部分）：
/// 把目录快照（扩展/MCP/Workflow/远程 Agent，经权限与健康过滤）渲染为
/// [Capabilities]/[Skills]/[RemoteAgents] 可读块，经 Contributor 管线统一注入。
/// 动态加载：能力较多时按与 UserInput 的关键词相关度裁剪（保留前 N 项）。
/// </summary>
public sealed class CapabilityContextContributor : IContextContributor
{
    private readonly ICapabilityCatalog _catalog;
    private const int MaxListed = 12;

    public CapabilityContextContributor(ICapabilityCatalog catalog) => _catalog = catalog;
    public string Id => "host.capabilities";

    public async Task<ContextContributionResult> ContributeAsync(
        ContextBuildRequest request,
        CancellationToken ct = default)
    {
        var snapshot = await _catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            SessionId = request.SessionId,
            SubjectId = request.SubjectId,
            TurnId = request.TurnId,
            Permissions = new CapabilityPermissions(),
            SceneTag = request.Extras.TryGetValue("scene", out var scene) ? scene?.ToString() : null,
            ProviderHealth = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        }, ct);

        var descriptors = snapshot.ByCanonicalId.Values
            .OrderBy(d => d.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (descriptors.Count == 0)
            return new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok }
            };

        var keyword = request.UserInput?.Trim() ?? string.Empty;
        var ranked = RankByRelevance(descriptors, keyword, MaxListed);
        var text = BuildBlock(ranked, descriptors.Count);
        return new ContextContributionResult
        {
            State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
            Contributions =
            [
                new ContextContribution
                {
                    Id = "capabilities", ContributorId = Id, Region = ContextRegion.Capabilities,
                    Content = text, Priority = 10, TokenEstimate = Math.Max(1, text.Length / 4)
                }
            ]
        };
    }

    private static List<CapabilityDescriptor> RankByRelevance(
        IReadOnlyList<CapabilityDescriptor> descriptors,
        string keyword,
        int max)
    {
        if (descriptors.Count <= max || string.IsNullOrWhiteSpace(keyword))
            return descriptors.Take(max).ToList();

        var terms = keyword.Split([' ', '，', '。', '？', '！', '、'], StringSplitOptions.RemoveEmptyEntries);
        return descriptors
            .OrderByDescending(d => Score(d, terms))
            .ThenBy(d => d.CanonicalId, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static int Score(CapabilityDescriptor descriptor, IReadOnlyList<string> terms)
    {
        var haystack = $"{descriptor.CanonicalId} {descriptor.Summary} {descriptor.Usage}".ToLowerInvariant();
        return terms.Sum(t => haystack.Contains(t.ToLowerInvariant()) ? 1 : 0);
    }

    private static string BuildBlock(IReadOnlyList<CapabilityDescriptor> listed, int total)
    {
        var lines = new List<string>();
        foreach (var descriptor in listed)
        {
            var description = string.IsNullOrWhiteSpace(descriptor.Usage)
                ? descriptor.Summary
                : $"{descriptor.Summary}。使用时机：{descriptor.Usage}";
            var kind = descriptor.Kind switch
            {
                CapabilityKind.Mcp => " (mcp)",
                CapabilityKind.Workflow => " (workflow)",
                CapabilityKind.RemoteAgent => " (agent)",
                _ => string.Empty
            };
            lines.Add($"- {descriptor.CanonicalId}{kind}: {description}");
        }
        if (total > listed.Count)
            lines.Add($"…共 {total} 个能力，其余按需查询");
        return string.Join('\n', lines);
    }
}
