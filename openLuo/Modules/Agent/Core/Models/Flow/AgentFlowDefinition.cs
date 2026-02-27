using System.Text.Json.Serialization;

namespace openLuo.Modules.Agent.Core.Models.Flow;

public sealed class AgentFlowDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("startNodeId")]
    public string StartNodeId { get; init; } = string.Empty;

    [JsonPropertyName("maxSteps")]
    public int MaxSteps { get; init; } = 16;

    [JsonPropertyName("nodes")]
    public IReadOnlyList<AgentFlowNode> Nodes { get; init; } = [];

    [JsonPropertyName("edges")]
    public IReadOnlyList<AgentFlowEdge> Edges { get; init; } = [];
}

public sealed class AgentFlowNode
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public AgentFlowNodeKind Kind { get; init; } = AgentFlowNodeKind.Executor;

    [JsonPropertyName("callName")]
    public string CallName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("inputMap")]
    public IReadOnlyDictionary<string, string> InputMap { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("outputKey")]
    public string OutputKey { get; init; } = string.Empty;
}

public sealed class AgentFlowEdge
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("fromNodeId")]
    public string FromNodeId { get; init; } = string.Empty;

    [JsonPropertyName("toNodeId")]
    public string ToNodeId { get; init; } = string.Empty;

    [JsonPropertyName("when")]
    public string When { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    [JsonPropertyName("guards")]
    public IReadOnlyList<AgentFlowGuard> Guards { get; init; } = [];
}

public sealed class AgentFlowGuard
{
    [JsonPropertyName("kind")]
    public AgentFlowGuardKind Kind { get; init; } = AgentFlowGuardKind.None;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter<AgentFlowNodeKind>))]
public enum AgentFlowNodeKind
{
    [JsonStringEnumMemberName("executor")] Executor,
    [JsonStringEnumMemberName("capability")] Capability,
    [JsonStringEnumMemberName("memory")] Memory,
    [JsonStringEnumMemberName("state")] State,
    [JsonStringEnumMemberName("terminal")] Terminal
}

[JsonConverter(typeof(JsonStringEnumConverter<AgentFlowGuardKind>))]
public enum AgentFlowGuardKind
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("output_exists")] OutputExists,
    [JsonStringEnumMemberName("output_equals")] OutputEquals,
    [JsonStringEnumMemberName("capability_available")] CapabilityAvailable,
    [JsonStringEnumMemberName("state_allows")] StateAllows,
    [JsonStringEnumMemberName("permission_allows")] PermissionAllows
}
