using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.AgentCapabilities.Core.Models;

public sealed class AgentCapabilityDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string[] Aliases { get; init; } = [];
    public string HelpShort { get; init; } = string.Empty;
    public string Category { get; init; } = "command";
    public string Prefix { get; init; } = "/";
    public string Usage { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "low";
    public bool NeedsConfirm { get; init; }
    public string[] Capabilities { get; init; } = [];
    public string ProviderId { get; init; } = string.Empty;
    public string ExecutorKind { get; init; } = "plugin";
}

public sealed class AgentKnownCharacter
{
    public string CharacterId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ArchetypeId { get; init; } = string.Empty;
}

public sealed class AgentCapabilityContext
{
    public string GameId { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
    public AgentExecutionContext? ExecutionContext { get; init; }
}

public sealed class AgentCapabilitySnapshot
{
    public IReadOnlyList<AgentCapabilityDescriptor> Capabilities { get; init; } = [];
    public IReadOnlyList<AgentKnownCharacter> KnownCharacters { get; init; } = [];
}
