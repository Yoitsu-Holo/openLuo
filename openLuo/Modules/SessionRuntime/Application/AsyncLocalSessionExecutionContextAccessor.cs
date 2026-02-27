using System.Threading;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class AsyncLocalSessionExecutionContextAccessor : ISessionExecutionContextAccessor
{
    private static readonly AsyncLocal<SessionExecutionContext?> CurrentContext = new();

    public SessionExecutionContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}
