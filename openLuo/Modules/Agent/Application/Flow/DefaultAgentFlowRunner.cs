using System.Text.Json;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class DefaultAgentFlowRunner : IAgentFlowRunner
{
    private readonly IAgentFlowRegistry _registry;
    private readonly IAgentFlowGuardEvaluator _guardEvaluator;
    private readonly IReadOnlyList<IAgentFlowNodeExecutor> _nodeExecutors;
    private readonly IExecutor<FlowRoutingInput, FlowRoutingOutput> _routingExecutor;
    private readonly IOutputEventBus? _outputEventBus;
    private readonly IRuntimeConfigCenter? _config;

    public DefaultAgentFlowRunner(
        IAgentFlowRegistry registry,
        IAgentFlowGuardEvaluator guardEvaluator,
        IEnumerable<IAgentFlowNodeExecutor> nodeExecutors,
        IExecutor<FlowRoutingInput, FlowRoutingOutput> routingExecutor,
        IOutputEventBus? outputEventBus = null,
        IRuntimeConfigCenter? config = null)
    {
        _registry = registry;
        _guardEvaluator = guardEvaluator;
        _nodeExecutors = nodeExecutors.ToList();
        _routingExecutor = routingExecutor;
        _outputEventBus = outputEventBus;
        _config = config;
    }

    public async Task<AgentFlowRunResult> RunAsync(AgentFlowRunRequest request, CancellationToken ct = default)
    {
        if (!_registry.TryGet(request.FlowId, out var definition))
            return AgentFlowRunResult.Fail($"Unknown flow '{request.FlowId}'.", request.Inputs, []);

        var state = new Dictionary<string, object?>(request.Inputs, StringComparer.OrdinalIgnoreCase);
        var steps = new List<AgentFlowStepResult>();
        var currentNodeId = definition.StartNodeId;
        var maxSteps = request.MaxStepsOverride ?? definition.MaxSteps;

        for (var iteration = 0; iteration < maxSteps; iteration++)
        {
            var node = definition.Nodes.FirstOrDefault(x => string.Equals(x.Id, currentNodeId, StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                await PublishTurnCompletedAsync(request, success: false, $"Flow node '{currentNodeId}' was not found.", ct);
                return AgentFlowRunResult.Fail($"Flow node '{currentNodeId}' was not found.", state, steps);
            }

            if (node.Kind == AgentFlowNodeKind.Terminal)
            {
                await PublishTurnCompletedAsync(request, success: true, error: null, ct);
                return AgentFlowRunResult.Ok(node.Id, state, steps);
            }

            request.ExecutionContext?.ReportProgress($"flow_node_start:{definition.Id}:{node.Id}");
            var executor = _nodeExecutors.FirstOrDefault(x => string.Equals(x.CallName, node.CallName, StringComparison.OrdinalIgnoreCase));
            if (executor is null)
            {
                await PublishTurnCompletedAsync(request, success: false, $"No node executor can handle '{node.CallName}'.", ct);
                return AgentFlowRunResult.Fail($"No node executor can handle '{node.CallName}'.", state, steps);
            }

            var execution = await executor.ExecuteAsync(node, request, state, ct);
            if (!execution.Success)
            {
                steps.Add(new AgentFlowStepResult
                {
                    NodeId = node.Id,
                    NodeKind = node.Kind,
                    Success = false,
                    OutputKey = node.OutputKey,
                    Error = execution.Error
                });
                await PublishTurnCompletedAsync(request, success: false, execution.Error, ct);
                return AgentFlowRunResult.Fail(execution.Error, state, steps);
            }

            request.ExecutionContext?.ReportProgress($"flow_node_done:{definition.Id}:{node.Id}");

            if (!string.IsNullOrWhiteSpace(node.OutputKey))
                state[node.OutputKey] = execution.Output;

            foreach (var pair in execution.StateUpdates)
                state[pair.Key] = pair.Value;

            var step = new AgentFlowStepResult
            {
                NodeId = node.Id,
                NodeKind = node.Kind,
                Success = true,
                OutputKey = node.OutputKey,
                Output = execution.Output
            };
            steps.Add(step);

            await PublishStepEventAsync(request, definition.Id, node, ct);

            var outgoing = definition.Edges.Where(x => string.Equals(x.FromNodeId, node.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (outgoing.Count == 0)
            {
                await PublishTurnCompletedAsync(request, success: true, error: null, ct);
                return AgentFlowRunResult.Ok(node.Id, state, steps);
            }

            var filtered = outgoing
                .Where(edge => edge.Guards.All(guard => _guardEvaluator.Allows(guard, request, state)))
                .ToList();

            if (filtered.Count == 0)
            {
                await PublishTurnCompletedAsync(request, success: false, $"No available edges after guard filtering from node '{node.Id}'.", ct);
                return AgentFlowRunResult.Fail($"No available edges after guard filtering from node '{node.Id}'.", state, steps);
            }

            AgentFlowEdge selected;
            if (filtered.Count == 1)
            {
                selected = filtered[0];
            }
            else
            {
                var executors = _config?.GetSnapshot().Executors;
                var routeResult = await _routingExecutor.ExecuteAsync(new FlowRoutingInput
                {
                    Temperature = executors?.FlowRouting?.Temperature,
                    MaxTokens = executors?.FlowRouting?.MaxTokens,
                    FlowId = definition.Id,
                    CurrentNodeId = node.Id,
                    PreviousNodeOutput = SerializeNodeOutput(execution.Output),
                    FlowStateSummary = BuildStateSummary(state),
                    Candidates = filtered.Select(x => new FlowRoutingCandidate
                    {
                        EdgeId = x.Id,
                        ToNodeId = x.ToNodeId,
                        When = x.When,
                        Priority = x.Priority
                    }).ToList()
                }, ct);

                if (!routeResult.Success || routeResult.Output is null)
                {
                    await PublishTurnCompletedAsync(request, success: false, $"Flow routing failed at node '{node.Id}': {routeResult.Error}", ct);
                    return AgentFlowRunResult.Fail($"Flow routing failed at node '{node.Id}': {routeResult.Error}", state, steps);
                }

                request.ExecutionContext?.ReportProgress($"flow_route_done:{definition.Id}:{node.Id}");

                var edgeId = routeResult.Output.SelectedEdgeId;
                selected = filtered.FirstOrDefault(x => string.Equals(x.Id, edgeId, StringComparison.OrdinalIgnoreCase))!;
                if (selected is null)
                {
                    await PublishTurnCompletedAsync(request, success: false, $"Flow router selected invalid edge '{edgeId}' at node '{node.Id}'.", ct);
                    return AgentFlowRunResult.Fail($"Flow router selected invalid edge '{edgeId}' at node '{node.Id}'.", state, steps);
                }

                if (!string.Equals(selected.ToNodeId, routeResult.Output.SelectedNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    await PublishTurnCompletedAsync(request, success: false, $"Flow router selected mismatched node '{routeResult.Output.SelectedNodeId}' for edge '{edgeId}'.", ct);
                    return AgentFlowRunResult.Fail($"Flow router selected mismatched node '{routeResult.Output.SelectedNodeId}' for edge '{edgeId}'.", state, steps);
                }
            }

            step.SelectedEdgeId = selected.Id;
            step.NextNodeId = selected.ToNodeId;
            currentNodeId = selected.ToNodeId;
        }

        await PublishTurnCompletedAsync(request, success: false, $"Flow '{definition.Id}' exceeded max steps {maxSteps}.", ct);
        return AgentFlowRunResult.Fail($"Flow '{definition.Id}' exceeded max steps {maxSteps}.", state, steps);
    }

    private async Task PublishStepEventAsync(
        AgentFlowRunRequest request,
        string flowId,
        AgentFlowNode node,
        CancellationToken ct)
    {
        if (_outputEventBus is null || string.IsNullOrWhiteSpace(request.SessionId))
            return;

        await _outputEventBus.PublishAsync(new AgentStepEvent
        {
            SessionId = request.SessionId,
            ChannelId = request.ChannelId ?? "system",
            EventId = Guid.NewGuid().ToString("N"),
            Kind = GameEventKind.AgentStep,
            Visibility = OutputVisibility.Debug,
            FlowId = flowId,
            NodeId = node.Id,
            NodeKind = node.Kind.ToString(),
            CallName = node.CallName,
            Description = node.Description
        }, ct);
    }

    private static string SerializeNodeOutput(object? value)
    {
        if (value is null)
            return "<null>";

        return value is string text
            ? text
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildStateSummary(IReadOnlyDictionary<string, object?> state)
    {
        var keys = state.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return keys.Count == 0
            ? "<empty>"
            : string.Join("\n", keys.Select(key => $"- {key}"));
    }

    private static async Task PublishTurnCompletedAsync(
        AgentFlowRunRequest request,
        bool success,
        string? error,
        CancellationToken ct)
    {
        if (request.MessageEmitter is null ||
            string.IsNullOrWhiteSpace(request.TurnId) ||
            string.IsNullOrWhiteSpace(request.SessionId) ||
            string.IsNullOrWhiteSpace(request.ChannelId))
            return;

        await request.MessageEmitter.PublishAsync(new TurnMessage
        {
            TurnId = request.TurnId,
            SessionId = request.SessionId,
            ChannelId = request.ChannelId,
            GameId = request.GameId,
            NodeId = "flow.completed",
            Kind = TurnMessageKind.Completed,
            Success = success,
            Error = error
        }, ct);
    }
}
