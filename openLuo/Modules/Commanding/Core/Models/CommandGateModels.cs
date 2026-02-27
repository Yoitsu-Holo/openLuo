using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.Commanding.Core.Models;

/// <summary>Context passed into command gate checks.</summary>
public sealed class CommandGateContext
{
    public string GameId { get; set; } = string.Empty;

    public string RawInput { get; set; } = string.Empty;

    public string CommandName { get; set; } = string.Empty;

    public string[] Args { get; set; } = [];

    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Result returned by command gate before command execution.</summary>
public sealed class CommandGateBeforeResult
{
    public bool Allow { get; set; } = true;

    public string? Message { get; set; }

    public List<string> Notices { get; set; } = [];

    public List<TimelineEvent> DueEvents { get; set; } = [];
}
