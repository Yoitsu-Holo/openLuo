using System.Threading;

namespace openLuo.Core.Models;

public sealed class GameRuntimeContext
{
    private static readonly AsyncLocal<GameRuntimeContext?> _current = new();

    public static GameRuntimeContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    public string? GameId { get; init; }
}
