using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public static class CharacterStandardChatFlow
{
    public const string FlowId = "character.standard_chat";

    public static AgentFlowDefinition Create() => new()
    {
        Id = FlowId,
        Description = "Built-in standard character chat turn flow.",
        StartNodeId = "memoryRecall",
        MaxSteps = 8,
        Nodes =
        [
            new AgentFlowNode
            {
                Id = "memoryRecall",
                Kind = AgentFlowNodeKind.Memory,
                CallName = "character.memory_recall",
                Description = "Load character memory for this turn.",
                OutputKey = "memory"
            },
            new AgentFlowNode
            {
                Id = "todoList",
                Kind = AgentFlowNodeKind.Executor,
                CallName = "character.todo_list",
                Description = "Generate a goal list for this turn (no tool selection).",
                OutputKey = "todoList"
            },
            new AgentFlowNode
            {
                Id = "exec",
                Kind = AgentFlowNodeKind.Capability,
                CallName = "character.exec",
                Description = "Execute each goal: select tools, run them, supervise, then generate final response.",
                OutputKey = "exec"
            },
            new AgentFlowNode
            {
                Id = "stateUpdate",
                Kind = AgentFlowNodeKind.State,
                CallName = "character.state_update",
                Description = "Produce state delta and final turn result.",
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
                Id = "memory-to-todoList",
                FromNodeId = "memoryRecall",
                ToNodeId = "todoList",
                When = "Memory recall completed."
            },
            new AgentFlowEdge
            {
                Id = "todolist-to-exec",
                FromNodeId = "todoList",
                ToNodeId = "exec",
                When = "Goals generated. Enter goal execution stage."
            },
            new AgentFlowEdge
            {
                Id = "exec-to-state-update",
                FromNodeId = "exec",
                ToNodeId = "stateUpdate",
                When = "Goal execution completed."
            },
            new AgentFlowEdge
            {
                Id = "state-update-to-done",
                FromNodeId = "stateUpdate",
                ToNodeId = "done",
                When = "State update completed."
            }
        ]
    };
}
