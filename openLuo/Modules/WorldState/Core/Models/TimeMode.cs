namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>Runtime time mode for the game session.</summary>
public enum TimeMode
{
    /// <summary>Game clock is driven by in-game actions (default).</summary>
    Virtual,

    /// <summary>Game clock is driven by wall-clock time.</summary>
    Realtime,

    /// <summary>Time system is disabled (pure dialogue agent mode).</summary>
    Disabled
}
