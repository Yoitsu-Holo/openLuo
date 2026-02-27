using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class OutputEventBusTurnMessageEmitter(
    IOutputEventBus outputEventBus) : ITurnMessageEmitter
{
    public bool HasPublishedPublicMessage { get; private set; }

    public async Task PublishAsync(TurnMessage message, CancellationToken ct = default)
    {
        switch (message.Kind)
        {
            case TurnMessageKind.Message when message.Message != Message.Empty && message.Message.Blocks.Count > 0:
                if (message.Message.Visibility == OutputVisibility.Public)
                    HasPublishedPublicMessage = true;

                await outputEventBus.PublishAsync(new MessageEvent
                {
                    SessionId = message.SessionId,
                    ChannelId = message.ChannelId,
                    EventId = Guid.NewGuid().ToString("N"),
                    Kind = GameEventKind.MessageOutput,
                    Visibility = message.Message.Visibility,
                    MessageId = message.Message.MessageId,
                    SpeakerRole = message.Message.SpeakerRole,
                    SpeakerId = message.Message.SpeakerId,
                    Blocks = message.Message.Blocks
                }, ct);
                break;
            case TurnMessageKind.Completed:
                await outputEventBus.PublishAsync(new TurnCompletedEvent
                {
                    SessionId = message.SessionId,
                    ChannelId = message.ChannelId,
                    EventId = Guid.NewGuid().ToString("N"),
                    Kind = GameEventKind.TurnCompleted,
                    Visibility = OutputVisibility.Debug,
                    TurnId = message.TurnId,
                    Success = message.Success,
                    Error = message.Error
                }, ct);
                break;
        }
    }
}
