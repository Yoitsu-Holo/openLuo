namespace openLuo.Core.Models;

/// <summary>
/// Context for dialogue interactions between player and character, or character-to-character.
/// </summary>
public class DialogueContext
{
    /// <summary>ID of the dialogue initiator (player or character).</summary>
    public string InitiatorId { get; set; } = "";

    /// <summary>Type of initiator (player or character).</summary>
    public DialogueParticipantType InitiatorType { get; set; }

    /// <summary>Target character ID for character-to-character dialogue (optional).</summary>
    public string? TargetCharacterId { get; set; }

    /// <summary>Dialogue message content.</summary>
    public string Message { get; set; } = "";

    /// <summary>Additional system context for LLM prompt injection.</summary>
    public List<string> SystemContexts { get; set; } = [];

    /// <summary>Whether player has no response (NPC-initiated dialogue).</summary>
    public bool PlayerNoResponse { get; set; }
}

/// <summary>
/// Type of dialogue participant.
/// </summary>
public enum DialogueParticipantType
{
    /// <summary>Player character.</summary>
    Player,

    /// <summary>NPC character.</summary>
    Character
}
