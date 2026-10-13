using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 已加载技能的**引用展开**（D30）：把"本对话加载过的技能 id"在这一轮上下文里展开成正文。
/// <para>
/// 这是"引用而非内联"的落点——`core:load_skill` 只记 id（引用），正文由本贡献者在**每个请求**构造时
/// 从技能库取最新内容拼进系统块，于是：上下文里永远是当前版本、正文不被当成对话内容、
/// 并且走系统块预算（<see cref="ContextRegion.LoadedSkills"/> 声明为可裁）。
/// </para>
/// <para>
/// 这里**不**替代 <c>core:load_skill</c>：不加载 = 不展开（模型按需决定要不要把某技能的正文带进上下文）。
/// </para>
/// </summary>
public sealed class LoadedSkillsContributor(ILoadedSkillSet loaded, ISkillService skills) : IContextContributor
{
    public string Id => "host.loaded-skills";

    /// <summary>按对话维度的内容（不同会话/对话各有各的加载状态）。</summary>
    public ContextScope Scope => ContextScope.Session;

    public async Task<ContextContributionResult> ContributeAsync(
        ContextBuildRequest request, CancellationToken ct = default)
    {
        var key = ConversationKeys.For(request.ConversationId, request.SessionId);
        var ids = loaded.Get(key);
        if (ids.Count == 0)
            return Ok(null);

        var blocks = new List<string>();
        foreach (var id in ids)
        {
            // 技能可能已在装载期被删掉（或 id 写错）→ 静默跳过，不为一个失效引用拖垮整块
            var document = await skills.LoadFullAsync(id, ct);
            if (document is null)
                continue;

            var block = $"### {document.Title} ({document.Id})\n{document.FullContent}";
            if (document.Constraints.Count > 0)
                block += $"\n约束：{string.Join("；", document.Constraints)}";
            blocks.Add(block);
        }

        if (blocks.Count == 0)
            return Ok(null);

        var content = "以下是本对话已加载技能的正文（按需加载、以引用展开；不再需要时可由模型卸载）：\n\n"
                      + string.Join("\n\n", blocks);
        return Ok(new ContextContribution
        {
            Id = "loaded-skills",
            ContributorId = Id,
            Region = ContextRegion.LoadedSkills,
            Content = content,
            // 介于人格(100)与记忆(40)之间：它是"角色此刻要做的事"的执行细节，比记忆更该留
            Priority = 45,
        });
    }

    private ContextContributionResult Ok(ContextContribution? contribution) => new()
    {
        State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
        Contributions = contribution is null ? [] : [contribution],
    };
}
