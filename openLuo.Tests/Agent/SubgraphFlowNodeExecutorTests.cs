using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Agent.Tests;

public sealed class SubgraphFlowNodeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RunsRegisteredChildFlow_AndExportsRequestedOutput()
    {
        var flowRunner = Substitute.For<IAgentFlowRunner>();
        flowRunner.RunAsync(Arg.Any<AgentFlowRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(AgentFlowRunResult.Ok(
                "done",
                new Dictionary<string, object?> { ["finalReply"] = "child-reply" },
                []));

        var sut = new SubgraphFlowNodeExecutor(flowRunner);

        var result = await sut.ExecuteAsync(
            new AgentFlowNode
            {
                Id = "childNode",
                CallName = "flow.subgraph",
                InputMap = new Dictionary<string, string>
                {
                    ["flowId"] = "demo.child",
                    ["inheritKeys"] = "turnContext,plan",
                    ["exportOutputKey"] = "finalReply"
                }
            },
            new AgentFlowRunRequest
            {
                FlowId = "parent.flow",
                AgentId = "c1",
                GameId = "g1",
                Inputs = new Dictionary<string, object?>()
            },
            new Dictionary<string, object?>
            {
                ["turnContext"] = new object(),
                ["plan"] = "plan-output",
                ["ignored"] = 123
            });

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.True(result.StateUpdates.ContainsKey("subgraph:demo.child"));
        Assert.Equal("child-reply", result.StateUpdates["finalReply"]);

        await flowRunner.Received(1).RunAsync(
            Arg.Is<AgentFlowRunRequest>(x =>
                x.FlowId == "demo.child" &&
                x.AgentId == "c1" &&
                x.GameId == "g1" &&
                x.Inputs.ContainsKey("turnContext") &&
                x.Inputs.ContainsKey("plan") &&
                !x.Inputs.ContainsKey("ignored")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FailsOnSelfRecursion()
    {
        var flowRunner = Substitute.For<IAgentFlowRunner>();
        var sut = new SubgraphFlowNodeExecutor(flowRunner);

        var result = await sut.ExecuteAsync(
            new AgentFlowNode
            {
                Id = "selfNode",
                CallName = "flow.subgraph",
                InputMap = new Dictionary<string, string>
                {
                    ["flowId"] = "same.flow"
                }
            },
            new AgentFlowRunRequest
            {
                FlowId = "same.flow",
                AgentId = "c1",
                GameId = "g1",
                Inputs = new Dictionary<string, object?>()
            },
            new Dictionary<string, object?>());

        Assert.False(result.Success);
        Assert.Contains("cannot invoke the same flow", result.Error);
        await flowRunner.DidNotReceive().RunAsync(Arg.Any<AgentFlowRunRequest>(), Arg.Any<CancellationToken>());
    }
}
