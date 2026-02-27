using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Agent.Tests;

public sealed class AgentFlowRegistryTests
{
    [Fact]
    public void DefaultRegistry_ContainsBuiltInCharacterFlow()
    {
        var registry = new DefaultAgentFlowRegistry();

        var found = registry.TryGet(CharacterStandardChatFlow.FlowId, out var definition);

        Assert.True(found);
        Assert.Equal("memoryRecall", definition.StartNodeId);
    }

    [Fact]
    public void Register_DuplicateFlowId_Throws()
    {
        var registry = new DefaultAgentFlowRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(CharacterStandardChatFlow.Create()));
    }

    [Fact]
    public void Register_ExternalRegistration_UsesNodeIdAndCallNameContract()
    {
        var registry = new DefaultAgentFlowRegistry();

        registry.Register(new AgentFlowRegistration
        {
            Id = "demo.external",
            StartNodeId = "plan",
            Nodes =
            [
                new AgentFlowRegistrationNode { Id = "plan", CallName = "character.plan" },
                new AgentFlowRegistrationNode { Id = "done", CallName = "terminal.done" }
            ],
            Edges =
            [
                new AgentFlowRegistrationEdge
                {
                    FromNodeId = "plan",
                    ToNodeId = "done",
                    When = "规划完成后结束"
                }
            ]
        });

        var found = registry.TryGet("demo.external", out var definition);

        Assert.True(found);
        Assert.Equal("character.plan", definition.Nodes.Single(x => x.Id == "plan").CallName);
        Assert.Equal("plan", definition.Edges.Single().FromNodeId);
        Assert.Equal("done", definition.Edges.Single().ToNodeId);
        Assert.Equal("规划完成后结束", definition.Edges.Single().When);
    }
}

// AgentFlowRunnerTests removed: the flow structure changed from
// memoryRecall→plan→plannedExecution→stateUpdate to memoryRecall→todoList→exec→stateUpdate,
// and the PlanOutput / CharacterPlannedExecutionResult types no longer exist.
// A new flow runner integration test should be written when the new flow stabilizes.
