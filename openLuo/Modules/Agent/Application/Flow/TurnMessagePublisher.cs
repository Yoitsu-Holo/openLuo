using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Application;

public static class TurnMessagePublisher
{
    public static async Task PublishPresentationAsync(
        AgentFlowNode node,
        AgentFlowRunRequest request,
        CommandPresentation presentation,
        CancellationToken ct)
    {
        if (request.MessageEmitter is null ||
            string.IsNullOrWhiteSpace(request.TurnId) ||
            string.IsNullOrWhiteSpace(request.SessionId) ||
            string.IsNullOrWhiteSpace(request.ChannelId) ||
            presentation.Messages.Count == 0)
            return;

        foreach (var message in presentation.Messages)
        {
            if (message == Message.Empty || message.Blocks.Count == 0)
                continue;

            await request.MessageEmitter.PublishAsync(new TurnMessage
            {
                TurnId = request.TurnId,
                SessionId = request.SessionId,
                ChannelId = request.ChannelId,
                GameId = request.GameId,
                NodeId = node.Id,
                Kind = TurnMessageKind.Message,
                Message = message
            }, ct);
        }
    }
}
