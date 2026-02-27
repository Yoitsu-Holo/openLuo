using openLuo.Core.Models;

namespace openLuo.Core;

/// <summary>
/// Central repository for all magic numbers and constants used throughout the game.
/// </summary>
public static class GameConstants
{
    // ===== Time System =====
    /// <summary>Default starting time of day in minutes (08:00).</summary>
    public const int DefaultStartMinute = 480;

    /// <summary>Minutes per day (24 hours).</summary>
    public const int MinutesPerDay = 1440;

    /// <summary>Default starting day.</summary>
    public const int DefaultStartDay = 1;

    // ===== Memory System =====
    /// <summary>Default number of memories to recall in search queries.</summary>
    public const int MemoryDefaultTopK = 5;

    /// <summary>Number of recent memories to retrieve.</summary>
    public const int MemoryRecentCount = 20;

    /// <summary>Number of high-emotional-weight memories to retrieve.</summary>
    public const int MemoryHighEmotionalCount = 10;

    /// <summary>Combined memory search result limit.</summary>
    public const int MemorySearchResultLimit = 15;

    /// <summary>Threshold for high emotional weight (absolute value).</summary>
    public const int MemoryHighEmotionalThreshold = 7;

    /// <summary>Number of old memories to check for compression.</summary>
    public const int MemoryCompressionCheckCount = 50;

    /// <summary>Days to look back for memory compression.</summary>
    public const int MemoryCompressionLookbackDays = 30;

    // ===== Affection System =====
    /// <summary>Maximum affection value.</summary>
    public const int AffectionMax = 1000;

    /// <summary>Affection threshold for Lover stage.</summary>
    public const int AffectionLoverThreshold = 800;

    /// <summary>Affection threshold for Close Friend stage.</summary>
    public const int AffectionCloseFriendThreshold = 600;

    /// <summary>Affection threshold for Friend stage.</summary>
    public const int AffectionFriendThreshold = 400;

    /// <summary>Affection threshold for Acquaintance stage.</summary>
    public const int AffectionAcquaintanceThreshold = 200;

    /// <summary>Maps affection value to relationship stage.</summary>
    public static RelationshipStage GetRelationshipStageForAffection(int affection) => affection switch
    {
        >= AffectionLoverThreshold => RelationshipStage.Lover,
        >= AffectionCloseFriendThreshold => RelationshipStage.CloseFriend,
        >= AffectionFriendThreshold => RelationshipStage.Friend,
        >= AffectionAcquaintanceThreshold => RelationshipStage.Acquaintance,
        _ => RelationshipStage.Stranger
    };

    // ===== Plugin System =====
    /// <summary>Default timeout for plugin commands in seconds.</summary>
    public const int PluginDefaultTimeoutSeconds = 30;

    /// <summary>Timeout for chat commands in seconds.</summary>
    public const int PluginChatTimeoutSeconds = 120;

    // ===== HTTP Configuration =====
    /// <summary>HTTP client timeout in seconds.</summary>
    public const int HttpTimeoutSeconds = 60;

    // ===== LLM Configuration =====
    /// <summary>Default temperature for LLM responses.</summary>
    public const float DefaultTemperature = 0.7f;

    /// <summary>Default embedding model.</summary>
    public const string DefaultEmbeddingModel = "text-embedding-3-small";

    /// <summary>Embedding request timeout in seconds.</summary>
    public const int EmbeddingTimeoutSeconds = 3;

    // ===== Emotional Weight Range =====
    /// <summary>Minimum emotional weight for memories.</summary>
    public const int MinEmotionalWeight = -10;

    /// <summary>Maximum emotional weight for memories.</summary>
    public const int MaxEmotionalWeight = 10;
}
