namespace openLuo.Modules.Commanding.Core.Models;

/// <summary>
/// Neutral command metadata shared across built-in and plugin-provided capabilities.
/// </summary>
public sealed class CommandDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = [];
    public string HelpShort { get; set; } = string.Empty;
    public string Category { get; set; } = "command";
    public string Prefix { get; set; } = "/";
    public string Usage { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "low";
    public bool NeedsConfirm { get; set; }
    public string[] Capabilities { get; set; } = [];
    public string ProviderId { get; set; } = string.Empty;
}
