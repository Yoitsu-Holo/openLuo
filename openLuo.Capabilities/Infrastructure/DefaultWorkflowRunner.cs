using System.Collections.Concurrent;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Infrastructure;

/// <summary>
/// 默认 Workflow 运行时（D13）。黑盒执行：确定性节点推进 + handler 分发 + 语义守卫。
/// </summary>
public sealed class DefaultWorkflowRunner : IWorkflowRunner
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, IWorkflowNodeHandler> _handlers;

    public DefaultWorkflowRunner(IEnumerable<IWorkflowNodeHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.HandlerId, StringComparer.OrdinalIgnoreCase);
    }

    public void Register(WorkflowDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("Workflow id is required", nameof(definition));
        _definitions[definition.Id] = definition;
    }

    public async Task<WorkflowRunResult> RunAsync(WorkflowRunRequest request, CancellationToken ct = default)
    {
        if (!_definitions.TryGetValue(request.WorkflowId, out var definition))
            return new WorkflowRunResult { Success = false, Error = $"unknown workflow: {request.WorkflowId}" };

        var state = new Dictionary<string, object?>(request.Input, StringComparer.OrdinalIgnoreCase);
        var steps = new List<string>();
        var currentNodeId = definition.StartNodeId;

        for (var step = 0; step < definition.MaxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            var node = definition.Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, currentNodeId, StringComparison.OrdinalIgnoreCase));
            if (node is null)
                return new WorkflowRunResult { Success = false, Error = $"workflow node not found: {currentNodeId}" };

            if (string.Equals(node.Kind, "terminal", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add($"terminal:{node.Id}");
                return new WorkflowRunResult
                {
                    Success = true,
                    TerminalNodeId = node.Id,
                    Outputs = state,
                    Steps = steps
                };
            }

            if (string.IsNullOrWhiteSpace(node.HandlerId) ||
                !_handlers.TryGetValue(node.HandlerId, out var handler))
                return new WorkflowRunResult { Success = false, Error = $"no handler for node {node.Id} (handler: {node.HandlerId})" };

            var result = await handler.ExecuteAsync(node, request, state, ct);
            steps.Add($"step:{node.Id}:{(result.Success ? "ok" : "fail")}");

            if (!result.Success)
                return new WorkflowRunResult { Success = false, Error = result.Error ?? $"node failed: {node.Id}" };

            foreach (var (key, value) in result.Outputs)
                state[key] = value;

            // 选择下一节点：显式 NextNodeId 或按边守卫选择
            var next = result.NextNodeId;
            if (string.IsNullOrWhiteSpace(next))
            {
                var edge = definition.Edges.FirstOrDefault(e =>
                    string.Equals(e.FromNodeId, node.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(e.Guard));
                next = edge?.ToNodeId ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(next))
                return new WorkflowRunResult { Success = false, Error = $"no outgoing edge from node {node.Id}" };

            currentNodeId = next;
        }

        return new WorkflowRunResult { Success = false, Error = $"workflow max steps exceeded: {definition.MaxSteps}", Steps = steps };
    }
}
