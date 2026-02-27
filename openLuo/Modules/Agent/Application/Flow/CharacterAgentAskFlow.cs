using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public static class CharacterAgentAskFlow
{
    public const string FlowId = "character.agent_ask";

    public static AgentFlowDefinition Create() => new()
    {
        Id = FlowId,
        Description = "Lightweight internal ask flow used for agent-to-agent consultation.",
        StartNodeId = "memoryRecall",
        MaxSteps = 6,
        Nodes =
        [
            new AgentFlowNode
            {
                Id = "memoryRecall",
                Kind = AgentFlowNodeKind.Memory,
                CallName = "character.memory_recall",
                Description = "Load minimal memory for internal ask.",
                OutputKey = "memory"
            },
            new AgentFlowNode
            {
                Id = "plan",
                Kind = AgentFlowNodeKind.Executor,
                CallName = "character.plan",
                Description = "Plan the internal ask response.",
                OutputKey = "plan"
            },
            new AgentFlowNode
            {
                Id = "plannedExecution",
                Kind = AgentFlowNodeKind.Capability,
                CallName = "character.planned_execution",
                Description = "Run lightweight local execution plan for agent ask.",
                OutputKey = "plannedExecution"
            },
            new AgentFlowNode
            {
                Id = "finalize",
                Kind = AgentFlowNodeKind.Executor,
                CallName = "character.finalize_reply",
                Description = "Finalize lightweight reply without state update.",
                OutputKey = "turnResult"
            },
            new AgentFlowNode
            {
                Id = "done",
                Kind = AgentFlowNodeKind.Terminal,
                CallName = "terminal.done",
                Description = "Terminal node."
            }
        ],
        Edges =
        [
            new AgentFlowEdge
            {
                Id = "memory-to-plan",
                FromNodeId = "memoryRecall",
                ToNodeId = "plan",
                When = "Memory recall completed."
            },
            new AgentFlowEdge
            {
                Id = "plan-to-planned-execution",
                FromNodeId = "plan",
                ToNodeId = "plannedExecution",
                When = "Plan completed."
            },
            new AgentFlowEdge
            {
                Id = "planned-execution-to-finalize",
                FromNodeId = "plannedExecution",
                ToNodeId = "finalize",
                When = "Planned execution completed."
            },
            new AgentFlowEdge
            {
                Id = "finalize-to-done",
                FromNodeId = "finalize",
                ToNodeId = "done",
                When = "Reply finalized."
            }
        ]
    };
}
