using System.Threading;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;

namespace openLuo.Infrastructure.Runtime;

public sealed class AsyncLocalGameContextAccessor : IGameContextAccessor
{
    private static readonly AsyncLocal<GameRuntimeContext?> CurrentContext = new();

    public GameRuntimeContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}
