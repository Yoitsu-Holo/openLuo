using openLuo.Hosting;
using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Interfaces.CLI;
using openLuo.Interfaces.QQbot;
using openLuo.Interfaces.TUI;
using openLuo.Modules.Agent.Application;

var options = LaunchOptions.Parse(args);
if (options is null)
{
    return;
}

await using var runtime = await OpenLuoBootstrapper.BootstrapAsync(options.Mode);
if (runtime is null)
{
    return;
}

if (options.Mode is LaunchMode.Tui)
{
    var tuiApp = new TuiApplication(
        runtime.SessionCatalog,
        runtime.Session,
        runtime.Streams,
        runtime.State);
    await tuiApp.RunAsync();
    return;
}

if (options.Mode is LaunchMode.QqBot)
{
    var qqApp = new QqBotApplication(
        runtime.SessionCatalog,
        runtime.ServiceProvider.GetRequiredService<IQqBotConfigCenter>());
    await qqApp.RunAsync();
    return;
}

var cliApp = new CliApplication(
    runtime.SessionCatalog,
    runtime.Session,
    runtime.State);
await cliApp.RunAsync();
