using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Hosting;

public sealed class OpenLuoRuntimeContext : IAsyncDisposable
{
    private readonly RuntimeConfigCenter _configCenter;

    public OpenLuoRuntimeContext(
        RuntimeConfigCenter configCenter,
        ServiceProvider serviceProvider,
        IGameSessionCatalog sessionCatalog,
        IGameSession session,
        IGameStreams streams,
        GameState? state)
    {
        _configCenter = configCenter;
        ServiceProvider = serviceProvider;
        SessionCatalog = sessionCatalog;
        Session = session;
        Streams = streams;
        State = state;
    }

    public ServiceProvider ServiceProvider { get; }
    public IGameSessionCatalog SessionCatalog { get; }
    public IGameSession Session { get; }
    public IGameStreams Streams { get; }
    public GameState? State { get; }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        _configCenter.Dispose();
    }
}
