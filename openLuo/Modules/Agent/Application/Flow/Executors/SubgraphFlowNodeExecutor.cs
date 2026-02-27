using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using Microsoft.Extensions.DependencyInjection;

namespace openLuo.Modules.Agent.Application;

/// <summary>
/// Generic subgraph executor. It invokes another registered flow as a child flow,
/// but is not wired into the built-in character flow yet.
/// </summary>
public sealed class SubgraphFlowNodeExecutor : IAgentFlowNodeExecutor
{
    public string CallName => "flow.subgraph";

    private readonly IAgentFlowRunner? _flowRunner;
    private readonly IServiceProvider? _services;

    public SubgraphFlowNodeExecutor(IAgentFlowRunner flowRunner)
    {
        _flowRunner = flowRunner;
    }

    [ActivatorUtilitiesConstructor]
    public SubgraphFlowNodeExecutor(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<AgentFlowNodeExecutionResult> ExecuteAsync(
        AgentFlowNode node,
        AgentFlowRunRequest request,
        IReadOnlyDictionary<string, object?> state,
        CancellationToken ct = default)
    {
        if (!node.InputMap.TryGetValue("flowId", out var subgraphFlowId) || string.IsNullOrWhiteSpace(subgraphFlowId))
            return AgentFlowNodeExecutionResult.Fail("Subgraph node requires inputMap.flowId.");

        if (string.Equals(subgraphFlowId, request.FlowId, StringComparison.OrdinalIgnoreCase))
            return AgentFlowNodeExecutionResult.Fail("Subgraph node cannot invoke the same flow recursively.");

        var childInputs = BuildChildInputs(node, state);
        var childRequest = new AgentFlowRunRequest
        {
            FlowId = subgraphFlowId,
            AgentId = request.AgentId,
            GameId = request.GameId,
            Inputs = childInputs,
            MaxStepsOverride = ResolveMaxSteps(node.InputMap)
        };

        var flowRunner = _flowRunner ?? _services?.GetRequiredService<IAgentFlowRunner>()
            ?? throw new InvalidOperationException("SubgraphFlowNodeExecutor could not resolve IAgentFlowRunner.");

        var childResult = await flowRunner.RunAsync(childRequest, ct);
        if (!childResult.Success)
            return AgentFlowNodeExecutionResult.Fail($"Subgraph '{subgraphFlowId}' failed: {childResult.Error}");

        var stateUpdates = new Dictionary<string, object?>
        {
            [$"subgraph:{subgraphFlowId}"] = childResult
        };

        if (node.InputMap.TryGetValue("exportOutputKey", out var exportOutputKey) &&
            !string.IsNullOrWhiteSpace(exportOutputKey) &&
            childResult.Outputs.TryGetValue(exportOutputKey, out var exportedValue))
        {
            stateUpdates[exportOutputKey] = exportedValue;
        }

        return AgentFlowNodeExecutionResult.Ok(childResult, stateUpdates);
    }

    private static Dictionary<string, object?> BuildChildInputs(AgentFlowNode node, IReadOnlyDictionary<string, object?> state)
    {
        var childInputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (node.InputMap.TryGetValue("inheritAllInputs", out var inheritAllRaw) &&
            bool.TryParse(inheritAllRaw, out var inheritAll) &&
            inheritAll)
        {
            foreach (var pair in state)
                childInputs[pair.Key] = pair.Value;
            return childInputs;
        }

        if (node.InputMap.TryGetValue("inheritKeys", out var inheritKeysRaw) && !string.IsNullOrWhiteSpace(inheritKeysRaw))
        {
            foreach (var key in inheritKeysRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (state.TryGetValue(key, out var value))
                    childInputs[key] = value;
            }
        }

        return childInputs;
    }

    private static int? ResolveMaxSteps(IReadOnlyDictionary<string, string> inputMap)
    {
        if (!inputMap.TryGetValue("maxStepsOverride", out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        return int.TryParse(raw, out var parsed) ? parsed : null;
    }
}
