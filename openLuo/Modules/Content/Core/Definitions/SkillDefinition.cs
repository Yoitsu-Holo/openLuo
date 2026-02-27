namespace openLuo.Modules.Content.Core.Definitions;

public sealed class SkillDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.Skill;

    public string Category { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "low";
    public bool NeedsConfirm { get; set; }
    public List<string> CapabilityTags { get; set; } = [];
    public string Body { get; set; } = string.Empty;
}
