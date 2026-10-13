using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using openLuo.Composition;
using Xunit;

namespace openLuo.Tests.Skills;

/// <summary>
/// 「已加载技能」的两个半边（D30 引用方案）：
/// ① <see cref="InMemoryLoadedSkillSet"/> 只存 **id**，按**对话**分区、有上限、可卸载；
/// ② <see cref="LoadedSkillsContributor"/> 在构造请求时把引用**展开成正文**（因此上下文里永远是最新版本，
/// 且正文不被当成对话内容、走系统块预算）。
/// </summary>
public sealed class LoadedSkillsTests
{
    private static SkillDocument Document(string id, string body) => new()
    {
        Id = id, Title = $"标题-{id}", Summary = "s", WhenToUse = "w",
        FullContent = body, Constraints = ["不要提及自己是 AI"],
    };

    // ── 集合 ───────────────────────────────────────────────────────────────

    [Fact]
    public void MarkLoaded_TracksPerConversation()
    {
        var set = new InMemoryLoadedSkillSet();

        set.MarkLoaded("conv-a", "s1");
        set.MarkLoaded("conv-b", "s2");

        // 按对话分区：多端共享同一 conversationId 时共享加载状态，不同对话互不影响
        Assert.Equal(["s1"], set.Get("conv-a"));
        Assert.Equal(["s2"], set.Get("conv-b"));
        Assert.Empty(set.Get("conv-c"));
    }

    [Fact]
    public void MarkLoaded_EvictsLeastRecentlyUsedAtCapacity()
    {
        var set = new InMemoryLoadedSkillSet(capacity: 2);

        set.MarkLoaded("c", "s1");
        set.MarkLoaded("c", "s2");
        set.MarkLoaded("c", "s3");       // 超上限 → 淘汰最久未用的 s1

        Assert.Equal(["s2", "s3"], set.Get("c"));

        set.MarkLoaded("c", "s2");       // 再次加载 s2 → 刷新为最近使用
        set.MarkLoaded("c", "s4");       // 淘汰最久未用的 s3

        Assert.Equal(["s2", "s4"], set.Get("c"));
    }

    [Fact]
    public void MarkLoaded_SameIdTwice_DoesNotDuplicate()
    {
        var set = new InMemoryLoadedSkillSet();

        set.MarkLoaded("c", "s1");
        set.MarkLoaded("c", "s1");

        Assert.Equal(["s1"], set.Get("c"));
    }

    [Fact]
    public void Unload_ReportsWhetherItWasLoaded()
    {
        var set = new InMemoryLoadedSkillSet();
        set.MarkLoaded("c", "s1");

        Assert.True(set.Unload("c", "s1"));
        Assert.False(set.Unload("c", "s1"));    // 幂等：第二次没有可移除的
        Assert.Empty(set.Get("c"));
    }

    [Fact]
    public void ConversationKey_PrefersConversationId()
    {
        // 规则唯一定义处：会话键优先，为空退回 sessionId
        Assert.Equal("conv", ConversationKeys.For("conv", "sess"));
        Assert.Equal("sess", ConversationKeys.For(null, "sess"));
        Assert.Equal("sess", ConversationKeys.For("   ", "sess"));
    }

    // ── 贡献者（引用 → 正文）───────────────────────────────────────────────

    private static ContextBuildRequest Request(string conversationId = "conv-a") => new()
    {
        SessionId = "sess-1", SubjectId = "builtin-rin", ConversationId = conversationId, TurnId = "t1",
    };

    [Fact]
    public async Task Contributor_EmitsNothingWhenNothingLoaded()
    {
        var contributor = new LoadedSkillsContributor(
            new InMemoryLoadedSkillSet(), new DefaultSkillService([Document("s1", "正文")]));

        var result = await contributor.ContributeAsync(Request());

        // 未加载就不占预算（这正是"目录 + 按需"的意义）
        Assert.Empty(result.Contributions);
        Assert.Equal(ContextSourceStatus.Ok, result.State.Status);
    }

    [Fact]
    public async Task Contributor_ExpandsLoadedIdsIntoBodyWithConstraints()
    {
        var loaded = new InMemoryLoadedSkillSet();
        loaded.MarkLoaded("conv-a", "s1");
        var contributor = new LoadedSkillsContributor(
            loaded, new DefaultSkillService([Document("s1", "正文甲"), Document("s2", "正文乙")]));

        var contribution = Assert.Single(
            (await contributor.ContributeAsync(Request())).Contributions);

        Assert.Equal(ContextRegion.LoadedSkills, contribution.Region);
        Assert.Contains("正文甲", contribution.Content);
        Assert.DoesNotContain("正文乙", contribution.Content);      // 没加载的不展开
        Assert.Contains("不要提及自己是 AI", contribution.Content); // 约束随正文一起注入
        // 按对话分区生效：别的对话看不到
        Assert.Empty((await contributor.ContributeAsync(Request("conv-other"))).Contributions);
    }

    [Fact]
    public async Task Contributor_ResolvesLatestBodyEachTime()
    {
        // 引用方案的核心好处：正文在**每个请求**重新展开 → 上下文里永远是当前版本
        var loaded = new InMemoryLoadedSkillSet();
        loaded.MarkLoaded("conv-a", "s1");
        var skills = new DefaultSkillService([Document("s1", "旧正文")]);
        var contributor = new LoadedSkillsContributor(loaded, skills);

        Assert.Contains("旧正文", (await contributor.ContributeAsync(Request())).Contributions[0].Content);

        var refreshed = new LoadedSkillsContributor(
            loaded, new DefaultSkillService([Document("s1", "新正文")]));
        Assert.Contains("新正文", (await refreshed.ContributeAsync(Request())).Contributions[0].Content);
    }

    [Fact]
    public async Task Contributor_SkipsIdsThatNoLongerExist()
    {
        // 技能在重启后可能已被删除 → 失效引用静默跳过，不拖垮整块
        var loaded = new InMemoryLoadedSkillSet();
        loaded.MarkLoaded("conv-a", "gone");
        var contributor = new LoadedSkillsContributor(loaded, new DefaultSkillService([]));

        Assert.Empty((await contributor.ContributeAsync(Request())).Contributions);
    }
}
