using openLuo.Core.Interfaces;

namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionGameEntry
{
    public required string GameId { get; init; }

    public string PlayerName { get; init; } = string.Empty;

    public string ArchetypeId { get; init; } = string.Empty;

    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class SessionCharacterRosterItem
{
    public required string CharacterId { get; init; }

    public required string ArchetypeId { get; init; }

    public required string Name { get; init; }

    public int DisplayPriority { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsActive { get; init; }
}

public sealed class SessionCharacterStatusSnapshot
{
    public required string CharacterId { get; init; }

    public required string ArchetypeId { get; init; }

    public required string CharacterName { get; init; }

    public int CurrentDay { get; init; }

    public int CurrentMinute { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlyList<StatusItem> Items { get; init; } = [];

    public string AdditionalText { get; init; } = string.Empty;
}
