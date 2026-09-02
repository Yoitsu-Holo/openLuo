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
                        Priority = priority,
                        TokenEstimate = content.Length / 4
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
    public async Task Build_RespectsTokenBudget()
    {
        var assembler = new DefaultContextAssembler(
        [
            new FakeContributor("a", ContextRegion.WorldContext, new string('x', 100)),
            new FakeContributor("b", ContextRegion.Identity, new string('y', 100))
        ], maxTokenBudget: 30);

        var context = await assembler.BuildAsync(new ContextBuildRequest
        {
            SessionId = "s1",
            SubjectId = "sub",
            TurnId = "t1"
        });

        Assert.Single(context.Contributions);
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
    public async Task LoadFull_CachesAndLists()
    {
        var service = new DefaultSkillService([Doc("gift"), Doc("math")]);

        var doc = await service.LoadFullAsync("gift");
        Assert.NotNull(doc);
        Assert.Equal("full-gift", doc!.FullContent);

        Assert.Contains("gift", service.ListLoaded());
        Assert.DoesNotContain("math", service.ListLoaded());
    }

    [Fact]
    public async Task Unload_RemovesFromCache()
    {
        var service = new DefaultSkillService([Doc("gift")]);
        await service.LoadFullAsync("gift");
        await service.UnloadAsync("gift");

        Assert.DoesNotContain("gift", service.ListLoaded());
    }

    [Fact]
    public async Task LoadFull_Unknown_ReturnsNull()
    {
        var service = new DefaultSkillService([]);
        Assert.Null(await service.LoadFullAsync("nope"));
    }

    [Fact]
    public async Task MaxLoaded_EvictsOldest()
    {
        var service = new DefaultSkillService([Doc("a"), Doc("b"), Doc("c")], maxLoaded: 2);
        await service.LoadFullAsync("a");
        await service.LoadFullAsync("b");
        await service.LoadFullAsync("c");

        Assert.Equal(2, service.ListLoaded().Count);
    }
}
