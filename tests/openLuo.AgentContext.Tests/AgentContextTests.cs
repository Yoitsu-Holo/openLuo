using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using Xunit;

namespace openLuo.AgentContext.Tests;

public class DefaultContextAssemblerTests
{
    private sealed class FakeContributor(string id, ContextRegion region, string content, int priority = 0) : IContextContributor
    {
        public string Id { get; } = id;
        public Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
                Contributions =
                [
                    new ContextContribution
                    {
                        Id = $"{id}:contrib",
                        ContributorId = Id,
                        Region = region,
                        Content = content,
                        Priority = priority
                    }
                ]
            });
    }

    private sealed class ThrowingContributor(string id) : IContextContributor
    {
        public string Id { get; } = id;
        public Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default) =>
            throw new TimeoutException("memory query timed out");
    }

    [Fact]
    public async Task Build_CollectsContributionsInOrder()
    {
        var assembler = new DefaultContextAssembler(
        [
            new FakeContributor("a", ContextRegion.WorldContext, "world-a"),
            new FakeContributor("b", ContextRegion.Identity, "identity-b")
        ]);

        var context = await assembler.BuildAsync(new ContextBuildRequest
        {
            SessionId = "s1",
            SubjectId = "sub",
            TurnId = "t1"
        });

        Assert.Equal(2, context.Contributions.Count);
        Assert.Contains(context.Contributions, c => c.ContributorId == "a");
        Assert.Contains(context.Contributions, c => c.ContributorId == "b");
    }

    [Fact]
    public async Task Build_ContributorThrows_DegradesToUnavailable()
    {
        var assembler = new DefaultContextAssembler(
        [
            new ThrowingContributor("memory"),
            new FakeContributor("companion", ContextRegion.Identity, "identity")
        ]);

        var context = await assembler.BuildAsync(new ContextBuildRequest
        {
            SessionId = "s1",
            SubjectId = "sub",
            TurnId = "t1"
        });

        // 失败来源以 Unavailable 结构化状态呈现，其他贡献正常
        var memoryState = Assert.Single(context.SourceStates, s => s.SourceId == "memory");
        Assert.Equal(ContextSourceStatus.Unavailable, memoryState.Status);
        Assert.True(memoryState.Retryable);
        Assert.Contains(context.Contributions, c => c.ContributorId == "companion");
    }

    [Fact]
    public async Task Build_PinnedRegionSurvivesEvenWhenOverBudget()
    {
        // 保底块（Identity）自身超上限：仍然保留，并显式标记 OverBudget + 记录被裁条目。
        var assembler = new DefaultContextAssembler(
        [
            new FakeContributor("a", ContextRegion.LongTermMemory, new string('x', 400), priority: 90),
            new FakeContributor("b", ContextRegion.Identity, new string('y', 400))
        ], limits: new StaticContextLimitsProvider(new ContextLimits { MaxTokens = 10 }));

        var context = await assembler.BuildAsync(new ContextBuildRequest
        {
            SessionId = "s1",
            SubjectId = "sub",
            TurnId = "t1"
        });

        var kept = Assert.Single(context.Contributions);
        Assert.Equal(ContextRegion.Identity, kept.Region);
        Assert.True(context.Budget.OverBudget);
        Assert.Equal(100, context.Budget.SystemTokens);              // 400 个窄字符 ≈ 100 token
        Assert.Equal(10, context.Budget.MaxTokens);
        var dropped = Assert.Single(context.Budget.DroppedContributions);
        Assert.Equal("a:contrib", dropped.Id);
        Assert.Equal(ContextRegion.LongTermMemory, dropped.Region);
    }

    [Fact]
    public async Task Build_DroppableSkipsOversizedItemAndKeepsSmallerOne()
    {
        // 旧实现按区域序 break：第一条放不下就整段丢弃，后面更小的条目也一起饿死。
        // 现在按优先级贪心 + 跳过，小条目仍能进。
        var assembler = new DefaultContextAssembler(
        [
            new FakeContributor("big", ContextRegion.SceneState, new string('x', 400), priority: 90),   // ≈100 token，放不下
            new FakeContributor("small", ContextRegion.LongTermMemory, new string('y', 40), priority: 10) // 10 token，应变裁但收下
        ], limits: new StaticContextLimitsProvider(new ContextLimits { MaxTokens = 30 }));

        var context = await assembler.BuildAsync(new ContextBuildRequest
        {
            SessionId = "s1",
            SubjectId = "sub",
            TurnId = "t1"
        });

        var kept = Assert.Single(context.Contributions);
        Assert.Equal("small", kept.ContributorId);
        Assert.False(context.Budget.OverBudget);
        Assert.Equal(10, context.Budget.SystemTokens);
        Assert.Equal("big:contrib", Assert.Single(context.Budget.DroppedContributions).Id);
    }

    [Fact]
    public async Task Build_KeepsPresentationOrderStable()
    {
        // 呈现序 = 区域枚举序 → 区域内 Priority 降序（稳定前缀，利于模型侧缓存）
        var assembler = new DefaultContextAssembler(
        [
            new FakeContributor("mem", ContextRegion.LongTermMemory, "m"),
            new FakeContributor("low", ContextRegion.Identity, "l", priority: 1),
            new FakeContributor("high", ContextRegion.Identity, "h", priority: 99)
        ]);

        var context = await assembler.BuildAsync(new ContextBuildRequest
        {
            SessionId = "s1",
            SubjectId = "sub",
            TurnId = "t1"
        });

        Assert.Equal(["high", "low", "mem"], context.Contributions.Select(c => c.ContributorId));
    }
}

public class DefaultMessageTagPipelineTests
{
    [Fact]
    public void Render_RegisteredRenderer_ProducesTag()
    {
        var pipeline = new DefaultMessageTagPipeline();
        pipeline.Register(new TypeTagRenderer());

        var tags = pipeline.Render(new Dictionary<string, string> { ["type"] = "card" });

        Assert.Equal(["[TYPE: card]"], tags);
    }

    [Fact]
    public void Render_UnregisteredKey_NoTag()
    {
        var pipeline = new DefaultMessageTagPipeline();
        pipeline.Register(new TypeTagRenderer());

        var tags = pipeline.Render(new Dictionary<string, string> { ["location"] = "教室" });

        Assert.Empty(tags);
    }

    [Fact]
    public void Compose_AddsTagsBeforeContent()
    {
        var pipeline = new DefaultMessageTagPipeline();
        var composed = pipeline.Compose("hello", "[TIME: 19:00]", ["[TYPE: card]"]);

        Assert.Equal("[TIME: 19:00] [TYPE: card] hello", composed);
    }

    [Fact]
    public void Strip_RemovesSemanticTags()
    {
        var pipeline = new DefaultMessageTagPipeline();
        var stripped = pipeline.Strip("[TIME: 19:00] [TYPE: card] hello");

        Assert.Equal("hello", stripped);
    }
}

public class DefaultSkillServiceTests
{
    private static SkillDocument Doc(string id) => new()
    {
        Id = id,
        Title = id,
        Summary = $"summary-{id}",
        FullContent = $"full-{id}"
    };

    [Fact]
    public async Task LoadFull_Unknown_ReturnsNull()
    {
        var service = new DefaultSkillService([]);
        Assert.Null(await service.LoadFullAsync("nope"));
    }


}
