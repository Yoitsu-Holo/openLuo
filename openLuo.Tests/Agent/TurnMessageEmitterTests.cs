using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.SessionRuntime.Application;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Agent.Tests;

public sealed class TurnMessageEmitterTests
{
    [Fact]
    public async Task OutputEventBusTurnMessageEmitter_PublishesMessageAndCompleted()
    {
        var bus = new InMemoryOutputEventBus();
        var sut = new OutputEventBusTurnMessageEmitter(bus);

        await sut.PublishAsync(new TurnMessage
        {
            TurnId = "t1",
            SessionId = "s1",
            ChannelId = "c1",
            GameId = "g1",
            NodeId = "plannedExecution",
            Kind = TurnMessageKind.Message,
            Message = Message.FromText("hello", speakerRole: "character", speakerId: "char1")
        });
        await sut.PublishAsync(new TurnMessage
        {
            TurnId = "t1",
            SessionId = "s1",
            ChannelId = "c1",
            GameId = "g1",
            NodeId = "flow.completed",
            Kind = TurnMessageKind.Completed,
            Success = true
        });

        var events = bus.Drain("s1");

        Assert.True(sut.HasPublishedPublicMessage);
        Assert.Collection(events,
            e =>
            {
                var msg = Assert.IsType<MessageEvent>(e);
                Assert.Equal(GameEventKind.MessageOutput, msg.Kind);
                Assert.Equal("character", msg.SpeakerRole);
            },
            e =>
            {
                var done = Assert.IsType<TurnCompletedEvent>(e);
                Assert.Equal(GameEventKind.TurnCompleted, done.Kind);
                Assert.True(done.Success);
                Assert.Equal("t1", done.TurnId);
            });
    }
}
