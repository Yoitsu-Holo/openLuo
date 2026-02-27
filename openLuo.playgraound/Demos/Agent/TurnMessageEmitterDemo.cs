using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.SessionRuntime.Application;

namespace openLuo.playgraound.Demos.Agent;

internal static class TurnMessageEmitterDemo
{
    public static async Task<int> RunAsync()
    {
        var bus = new InMemoryOutputEventBus();
        var emitter = new OutputEventBusTurnMessageEmitter(bus);

        await emitter.PublishAsync(new TurnMessage
        {
            TurnId = "demo-turn",
            SessionId = "demo-session",
            ChannelId = "demo-channel",
            GameId = "demo-game",
            NodeId = "plannedExecution",
            Kind = TurnMessageKind.Message,
            Message = Message.FromText("首包文本已经可以立即发送。", speakerRole: "character", speakerId: "demo-character")
        });

        await emitter.PublishAsync(new TurnMessage
        {
            TurnId = "demo-turn",
            SessionId = "demo-session",
            ChannelId = "demo-channel",
            GameId = "demo-game",
            NodeId = "flow.completed",
            Kind = TurnMessageKind.Completed,
            Success = true
        });

        Console.WriteLine("=== Turn Message Emitter Demo ===");
        foreach (var evt in bus.Drain("demo-session"))
        {
            Console.WriteLine($"{evt.Kind} / {evt.Visibility}");
        }

        return 0;
    }
}
