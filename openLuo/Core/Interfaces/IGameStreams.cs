namespace openLuo.Core.Interfaces;

/// <summary>
/// Provides standard I/O streams for game input/output operations.
/// </summary>
public interface IGameStreams
{
    /// <summary>Input stream for reading user commands.</summary>
    Stream Input { get; }

    /// <summary>Output stream for writing game responses.</summary>
    Stream Output { get; }

    /// <summary>Error stream for writing error messages.</summary>
    Stream Error { get; }
}
