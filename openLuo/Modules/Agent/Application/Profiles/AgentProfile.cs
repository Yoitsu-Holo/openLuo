namespace openLuo.Modules.Agent.Application;

public sealed class AgentProfile
{
    public string CharacterId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ArchetypeId { get; init; } = string.Empty;

    public string RolePrompt { get; init; } = string.Empty;
}
