using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Application;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Application.Tests;

public sealed class TurnMessageStreamingSurfaceTests
{
    [Fact]
    public async Task FlowRunner_PublishesPublicMessageThenCompleted()
    {
        var bus = new InMemoryOutputEventBus();
        var emitter = new OutputEventBusTurnMessageEmitter(bus);
        var runner = new DefaultAgentFlowRunner(
            new SingleFlowRegistry(),
            new DefaultAgentFlowGuardEvaluator(),
            [new StreamingNodeExecutor()],
            Substitute.For<IExecutor<FlowRoutingInput, FlowRoutingOutput>>(),
            bus);

        var result = await runner.RunAsync(new AgentFlowRunRequest
        {
            FlowId = "demo.streaming",
            AgentId = "char1",
            GameId = "game1",
            TurnId = "turn1",
            SessionId = "session1",
            ChannelId = "channel1",
            MessageEmitter = emitter,
            Inputs = new Dictionary<string, object?>()
        });

        Assert.True(result.Success);

        var events = bus.Drain("session1");
        Assert.Contains(events, e => e is AgentStepEvent step && step.NodeId == "emit");
        Assert.Contains(events, e => e is MessageEvent msg && msg.Kind == GameEventKind.MessageOutput && msg.SpeakerRole == "character");
        Assert.Contains(events, e => e is TurnCompletedEvent done && done.Kind == GameEventKind.TurnCompleted && done.Success);
    }

    private sealed class StreamingNodeExecutor : IAgentFlowNodeExecutor
    {
        public string CallName => "demo.emit";

        public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
            AgentFlowNode node,
            AgentFlowRunRequest request,
            IReadOnlyDictionary<string, object?> state,
            CancellationToken ct = default)
        {
            await TurnMessagePublisher.PublishPresentationAsync(
                node,
                request,
                Modules.Commanding.Core.Models.CommandPresentation.FromText("stream now", "character", "char1"),
                ct);

            return AgentFlowNodeExecutionResult.Ok("ok");
        }
    }

    private sealed class SingleFlowRegistry : IAgentFlowRegistry
    {
        private readonly AgentFlowDefinition _definition = new()
        {
            Id = "demo.streaming",
            StartNodeId = "emit",
            Nodes =
            [
                new AgentFlowNode
                {
                    Id = "emit",
                    Kind = AgentFlowNodeKind.Executor,
                    CallName = "demo.emit",
                    OutputKey = "reply"
                },
                new AgentFlowNode
                {
                    Id = "done",
                    Kind = AgentFlowNodeKind.Terminal,
                    CallName = "terminal.done"
                }
            ],
            Edges =
            [
                new AgentFlowEdge
                {
                    Id = "emit-done",
                    FromNodeId = "emit",
                    ToNodeId = "done",
                    When = "done"
                }
            ]
        };

        public void Register(AgentFlowDefinition definition) => throw new NotSupportedException();

        public void Register(AgentFlowRegistration registration) => throw new NotSupportedException();

        public bool TryGet(string flowId, out AgentFlowDefinition definition)
        {
            definition = _definition;
            return string.Equals(flowId, _definition.Id, StringComparison.OrdinalIgnoreCase);
        }

        public IReadOnlyList<AgentFlowDefinition> List() => [_definition];
    }
}
