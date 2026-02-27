using System.Text.Json;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Agent.Tests;

public sealed class AgentFlowModelTests
{
    [Fact]
    public void FlowDefinition_SerializesEnumNamesAsStrings()
    {
        var definition = new AgentFlowDefinition
        {
            Id = "character.chat",
            StartNodeId = "plan",
            Nodes =
            [
                new AgentFlowNode
                {
                    Id = "plan",
                    Kind = AgentFlowNodeKind.Executor,
                    CallName = "character.plan"
                }
            ],
            Edges =
            [
                new AgentFlowEdge
                {
                    Id = "plan-to-response",
                    FromNodeId = "plan",
                    ToNodeId = "characterResponse",
                    When = "不需要工具时进入角色回复",
                    Guards =
                    [
                        new AgentFlowGuard
                        {
                            Kind = AgentFlowGuardKind.OutputEquals,
                            Key = "plan.needTool",
                            Value = "false"
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(definition);

        Assert.Contains("\"kind\":\"executor\"", json);
        Assert.Contains("\"kind\":\"output_equals\"", json);
    }
}
