namespace openLuo.Core.Models;

// ── Message kind ──────────────────────────────────────────────────────────

/// <summary>
/// Conversation mode for SendMessage.
/// </summary>
public enum MessageKind
{
    /// <summary>Standard chat conversation.</summary>
    Chat,

    /// <summary>Date / romantic interaction mode.</summary>
    Date
}

// ── 1. ListGames ──────────────────────────────────────────────────────────

/// <summary>
/// Summary entry for an existing game save.
/// </summary>
public sealed class GameEntry
{
    public required string GameId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public string ArchetypeId { get; init; } = string.Empty;
    public DateTime UpdatedAtUtc { get; init; }
}

// ── 2. CreateGame ─────────────────────────────────────────────────────────

/// <summary>
/// Request to create a new game.
/// </summary>
public sealed class CreateGameRequest
{
    public required string PlayerName { get; init; }
    public required string ArchetypeId { get; init; }
    public string? RequestedGameId { get; init; }
}

/// <summary>
/// Result of creating a new game.
/// </summary>
public sealed class CreateGameResult
{
    public required string GameId { get; init; }
}

// ── 3. ListArchetypes ─────────────────────────────────────────────────────

/// <summary>
/// A character archetype (template) available for new games.
/// </summary>
public sealed class ArchetypeInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CharacterName { get; init; }
}

// ── 4. SendMessage ────────────────────────────────────────────────────────

/// <summary>
/// Request to send a message in a conversation.
/// Blocks use the unified <see cref="Block"/> multi-modal protocol.
/// </summary>
public sealed class SendMessageRequest
{
    public required string GameId { get; init; }
    public MessageKind Kind { get; init; } = MessageKind.Chat;
    public IReadOnlyList<Block> Blocks { get; init; } = [];
}

/// <summary>
/// Result of sending a message. Contains the game's response as Block[].
/// </summary>
public sealed class SendMessageResult
{
    public IReadOnlyList<Block> Blocks { get; init; } = [];

    /// <summary>How many in-game minutes advanced during this turn, if any.</summary>
    public int? TimeAdvancedMinutes { get; init; }
}

// ── 5. ExecuteCommand ─────────────────────────────────────────────────────

/// <summary>
/// Request to execute a command. This is the degradation path for commands
/// without a dedicated API endpoint.
/// </summary>
public sealed class CommandRequest
{
    public required string GameId { get; init; }
    public required string CommandText { get; init; }
}

/// <summary>
/// Result of a command execution. Always carries a warning since this is a
/// degradation path.
/// </summary>
public sealed class ExecuteCommandResult
{
    public IReadOnlyList<Block> Blocks { get; init; } = [];

    /// <summary>
    /// Always populated — indicates this command has no first-class API endpoint
    /// and is executing via the fallback path.
    /// </summary>
    public string Warning { get; init; } = string.Empty;
}

// ── 6. GetState ───────────────────────────────────────────────────────────

/// <summary>
/// Request to query the current game status.
/// </summary>
public sealed class GetStateRequest
{
    public required string GameId { get; init; }
}

/// <summary>
/// Current status of a character.
/// </summary>
public sealed class CharacterStatusEntry
{
    public required string CharacterId { get; init; }
    public required string Name { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<CharacterStatusItem> Items { get; init; } = [];
    public string AdditionalText { get; init; } = string.Empty;
}

/// <summary>
/// A single status item for a character (attribute bar, text value, etc.).
/// </summary>
public sealed class CharacterStatusItem
{
    public required string Label { get; init; }
    public required string Group { get; init; }
    public required string Value { get; init; }
    public string? Text { get; init; }
    public string? Max { get; init; }
    public string? Type { get; init; }
}

/// <summary>
/// Full game status snapshot.
/// </summary>
public sealed class GameStatus
{
    public required string GameId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public string ArchetypeId { get; init; } = string.Empty;
    public int CurrentDay { get; init; }
    public int CurrentMinute { get; init; }
    public string? ActiveCharacterId { get; init; }
    public IReadOnlyList<CharacterStatusEntry> Characters { get; init; } = [];
}
