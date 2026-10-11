using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Hosting;

public sealed class OpenLuoRuntimeContext : IAsyncDisposable
{
    public OpenLuoRuntimeContext(ServiceProvider serviceProvider, IGameStreams streams)
    {
        ServiceProvider = serviceProvider;
        Streams = streams;
    }

    public ServiceProvider ServiceProvider { get; }
    public IGameStreams Streams { get; }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
    }
}
