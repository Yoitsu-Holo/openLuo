namespace openLuo.Modules.PluginRuntime.Infrastructure;

/// <summary>
/// Internal command metadata parsed from plugin tool manifests.
/// </summary>
internal sealed class PluginCommandDef
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
    public string PluginId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
}
