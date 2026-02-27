using openLuo.Core.Models;

namespace openLuo.Modules.Commanding.Core.Models;

public enum InvocationKind
{
    Command,
    Skill,
    SubAgent,
    Tool
}

/// <summary>
/// Result of command execution.
/// </summary>
public class CommandResult
{
    /// <summary>Whether command executed successfully.</summary>
    public bool Success { get; set; } = true;

    /// <summary>Command output text.</summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Error message if command failed.</summary>
    public string? Error { get; set; }

    /// <summary>Structured presentation payload for host adapters.</summary>
    public CommandPresentation Presentation { get; set; } = CommandPresentation.Empty;

    /// <summary>Optional structured metadata for downstream consumers.</summary>
    public Dictionary<string, object?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Create a successful result from plain text.</summary>
    public static CommandResult Ok(string output) =>
        Ok(CommandPresentation.FromText(output));

    /// <summary>Create a successful result from a structured presentation.</summary>
    public static CommandResult Ok(CommandPresentation presentation) => new()
    {
        Output = presentation.ToPlainText(),
        Presentation = presentation
    };

    /// <summary>Create a failed result.</summary>
    /// <param name="error">Error message.</param>
    /// <returns>CommandResult with Success=false.</returns>
    public static CommandResult Fail(string error) => new() { Success = false, Error = error };
}

public static class CommandResultMetadataKeys
{
    public const string InterAgentOutcome = "interAgentOutcome";
    public const string StreamedPublicOutput = "streamedPublicOutput";
}

/// <summary>
/// Structured presentation consisting of one or more <see cref="Message"/>s.
/// </summary>
public sealed class CommandPresentation
{
    public static CommandPresentation Empty { get; } = new();

    public IReadOnlyList<Message> Messages { get; init; } = [];

    public static CommandPresentation FromText(string text, string speakerRole = "assistant", string? speakerId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        return new CommandPresentation
        {
            Messages = [Message.FromText(text, speakerRole, speakerId)]
        };
    }

    public string ToPlainText()
    {
        if (Messages.Count == 0)
            return string.Empty;

        return string.Join(
            "\n",
            Messages.Select(m => m.ToPlainText())
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }
}

/// <summary>
/// Parsed command with name, arguments, and options.
/// </summary>
public class ParsedCommand
{
    /// <summary>Invocation prefix kind: / command, $ skill, & subAgent, @ tool.</summary>
    public InvocationKind Kind { get; set; } = InvocationKind.Command;

    /// <summary>Raw prefix character.</summary>
    public char Prefix { get; set; } = '/';

    /// <summary>Command name (e.g., "chat", "give").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Positional arguments.</summary>
    public string[] Args { get; set; } = [];

    /// <summary>Named options (key-value pairs).</summary>
    public Dictionary<string, string> Options { get; set; } = [];
}

/// <summary>
/// Game context containing state and character information for command execution.
/// </summary>
public class GameContext
{
    /// <summary>Current game state.</summary>
    public required GameState State { get; set; }

    /// <summary>Current character being interacted with.</summary>
    public required Character Character { get; set; }
}
