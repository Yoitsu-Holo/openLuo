namespace openLuo.Capabilities.A2A;

/// <summary>A2A agent endpoint configuration (host-level config/a2a-agents.jsonc).</summary>
public sealed class A2AAgentConfig
{
    public string Id { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? AgentCardUrl { get; init; }
    public string? Tenant { get; init; }
}

/// <summary>A2A agent collection configuration.</summary>
public sealed class A2AAgentsConfig
{
    public IReadOnlyList<A2AAgentConfig> Agents { get; init; } = [];
}
