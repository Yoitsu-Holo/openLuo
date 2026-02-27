using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public sealed class DefaultAgentFlowRegistry : IAgentFlowRegistry
{
    private readonly Dictionary<string, AgentFlowDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public DefaultAgentFlowRegistry()
    {
        Register(CharacterStandardChatFlow.Create());
        Register(CharacterAgentAskFlow.Create());
    }

    public void Register(AgentFlowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinition(definition);
        if (_definitions.ContainsKey(definition.Id))
            throw new InvalidOperationException($"Flow '{definition.Id}' is already registered.");

        _definitions[definition.Id] = definition;
    }

    public void Register(AgentFlowRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Register(ToDefinition(registration));
    }

    public bool TryGet(string flowId, out AgentFlowDefinition definition)
    {
        if (_definitions.TryGetValue(flowId, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    public IReadOnlyList<AgentFlowDefinition> List() => _definitions.Values.ToList();

    private static void ValidateDefinition(AgentFlowDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("Flow definition id is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.StartNodeId))
            throw new ArgumentException("Flow startNodeId is required.", nameof(definition));
        if (definition.Nodes.Count == 0)
            throw new ArgumentException("Flow must contain at least one node.", nameof(definition));

        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in definition.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                throw new ArgumentException($"Flow '{definition.Id}' contains a node without id.", nameof(definition));
            if (string.IsNullOrWhiteSpace(node.CallName))
                throw new ArgumentException($"Flow '{definition.Id}' node '{node.Id}' is missing callName.", nameof(definition));
            if (!nodeIds.Add(node.Id))
                throw new ArgumentException($"Flow '{definition.Id}' contains duplicate node id '{node.Id}'.", nameof(definition));
        }

        if (!nodeIds.Contains(definition.StartNodeId))
            throw new ArgumentException($"Flow '{definition.Id}' startNodeId '{definition.StartNodeId}' does not exist.", nameof(definition));

        foreach (var edge in definition.Edges)
        {
            if (!nodeIds.Contains(edge.FromNodeId))
                throw new ArgumentException($"Flow '{definition.Id}' edge '{edge.Id}' has unknown fromNodeId '{edge.FromNodeId}'.", nameof(definition));
            if (!nodeIds.Contains(edge.ToNodeId))
                throw new ArgumentException($"Flow '{definition.Id}' edge '{edge.Id}' has unknown toNodeId '{edge.ToNodeId}'.", nameof(definition));
        }
    }

    private static AgentFlowDefinition ToDefinition(AgentFlowRegistration registration)
    {
        var nodes = registration.Nodes.Select(node => new AgentFlowNode
        {
            Id = node.Id,
            CallName = node.CallName,
            Kind = InferKind(node.CallName),
            Description = node.CallName,
            OutputKey = InferOutputKey(node.CallName)
        }).ToList();

        var edges = registration.Edges.Select((edge, index) => new AgentFlowEdge
        {
            Id = $"{edge.FromNodeId}-to-{edge.ToNodeId}-{index + 1}",
            FromNodeId = edge.FromNodeId,
            ToNodeId = edge.ToNodeId,
            When = edge.When
        }).ToList();

        return new AgentFlowDefinition
        {
            Id = registration.Id,
            Version = registration.Version,
            Description = registration.Description,
            StartNodeId = registration.StartNodeId,
            MaxSteps = registration.MaxSteps,
            Nodes = nodes,
            Edges = edges
        };
    }

    private static AgentFlowNodeKind InferKind(string callName)
    {
        if (callName.StartsWith("terminal.", StringComparison.OrdinalIgnoreCase))
            return AgentFlowNodeKind.Terminal;
        if (callName.Contains(".memory_", StringComparison.OrdinalIgnoreCase))
            return AgentFlowNodeKind.Memory;
        if (callName.Contains(".tool_", StringComparison.OrdinalIgnoreCase))
            return AgentFlowNodeKind.Capability;
        if (callName.Contains(".state_", StringComparison.OrdinalIgnoreCase))
            return AgentFlowNodeKind.State;
        return AgentFlowNodeKind.Executor;
    }

    private static string InferOutputKey(string callName) => callName.ToLowerInvariant() switch
    {
        "character.memory_recall" => "memory",
        "character.plan" => "plan",
        "character.tool_use" => "toolResult",
        "character.response" => "finalReply",
        "character.state_update" => "turnResult",
        _ => string.Empty
    };
}
