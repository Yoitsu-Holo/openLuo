using System.Text.Json.Serialization;

namespace openLuo.Modules.Agent.Core.Models.Flow;

public sealed class AgentFlowRegistration
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
    public IReadOnlyList<AgentFlowRegistrationNode> Nodes { get; init; } = [];

    [JsonPropertyName("edges")]
    public IReadOnlyList<AgentFlowRegistrationEdge> Edges { get; init; } = [];
}

public sealed class AgentFlowRegistrationNode
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("callName")]
    public string CallName { get; init; } = string.Empty;
}

public sealed class AgentFlowRegistrationEdge
{
    [JsonPropertyName("fromNodeId")]
    public string FromNodeId { get; init; } = string.Empty;

    [JsonPropertyName("toNodeId")]
    public string ToNodeId { get; init; } = string.Empty;

    [JsonPropertyName("when")]
    public string When { get; init; } = string.Empty;
}
