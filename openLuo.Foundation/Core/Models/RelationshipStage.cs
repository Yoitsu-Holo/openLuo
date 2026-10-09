namespace openLuo.Core.Models;

/// <summary>
/// Relationship progression stages between player and character.
/// </summary>
public enum RelationshipStage
{
    /// <summary>No prior interaction.</summary>
    Stranger,

    /// <summary>Initial acquaintance.</summary>
    Acquaintance,

    /// <summary>Friendly relationship.</summary>
    Friend,

    /// <summary>Close friendship.</summary>
    CloseFriend,

    /// <summary>Romantic relationship.</summary>
    Lover
}
