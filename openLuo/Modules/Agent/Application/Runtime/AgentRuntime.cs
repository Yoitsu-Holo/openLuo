using openLuo.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public interface IAgentMessageHandler
{
    Task<AgentMessage?> HandleAsync(AgentContext context, AgentMessage message, CancellationToken ct = default);
}

public interface ICharacterAgentRuntime
{
    string CharacterId { get; }

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}

public sealed class CharacterAgentRuntime : ICharacterAgentRuntime
{
    private readonly IAgentMailbox _mailbox;
    private readonly IAgentMessageHandler _handler;
    private readonly IAgentContextStore _contextStore;
    private readonly IGameLogger _logger;
    private readonly SemaphoreSlim _startStopLock = new(1, 1);
    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    public string CharacterId => _mailbox.CharacterId;

    public CharacterAgentRuntime(
        IAgentMailbox mailbox,
        IAgentMessageHandler handler,
        IAgentContextStore contextStore,
        IGameLogger logger)
    {
        _mailbox = mailbox;
        _handler = handler;
        _contextStore = contextStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _startStopLock.WaitAsync(ct);
        try
        {
            if (_loopTask is not null)
                return;

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), _loopCts.Token);
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _startStopLock.WaitAsync(ct);
        try
        {
            if (_loopTask is null || _loopCts is null)
                return;

            _loopCts.Cancel();
            try
            {
                await _loopTask.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _loopTask = null;
                _loopCts.Dispose();
                _loopCts = null;
            }
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await foreach (var item in _mailbox.ReadAllAsync(ct))
        {
            CancellationTokenSource? itemCts = null;
            try
            {
                var itemToken = item.HandlingToken.CanBeCanceled
                    ? (itemCts = CancellationTokenSource.CreateLinkedTokenSource(ct, item.HandlingToken)).Token
                    : ct;

                if (itemToken.IsCancellationRequested)
                {
                    item.ReplySink?.TrySetResult(null);
                    continue;
                }

                var context = await _contextStore.GetOrCreateAsync(item.Message.GameId, CharacterId, itemToken);
                if (ShouldAppendToConversation(item.Message.Type))
                    AppendTurn(context, item.Message.From, "inbound", item.Message.Payload, item.Message.TimestampUtc);
                var reply = await _handler.HandleAsync(context, item.Message, itemToken);
                if (reply is not null && !string.IsNullOrWhiteSpace(reply.Payload) && ShouldAppendToConversation(reply.Type))
                    AppendTurn(context, reply.From, "outbound", reply.Payload, reply.TimestampUtc);
                context.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await _contextStore.SaveAsync(context, itemToken);
                item.ReplySink?.TrySetResult(reply);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                item.ReplySink?.TrySetCanceled(ct);
                throw;
            }
            catch (OperationCanceledException)
            {
                item.ReplySink?.TrySetResult(null);
            }
            catch (Exception ex)
            {
                _logger.Error("agent/runtime", $"agent runtime failed: {CharacterId}", new { error = ex.Message, messageId = item.Message.MessageId });
                item.ReplySink?.TrySetResult(null);
            }
            finally
            {
                itemCts?.Dispose();
            }
        }
    }

    private static void AppendTurn(
        AgentContext context,
        string speakerId,
        string speakerRole,
        string content,
        DateTimeOffset timestampUtc)
    {
        context.Conversation.Add(new AgentConversationTurn
        {
            SpeakerId = speakerId,
            SpeakerRole = speakerRole,
            Content = content,
            TimestampUtc = timestampUtc
        });

        if (context.Conversation.Count > 32)
            context.Conversation.RemoveRange(0, context.Conversation.Count - 32);
    }

    private static bool ShouldAppendToConversation(AgentMessageType type) =>
        type is AgentMessageType.Chat
            or AgentMessageType.AgentAsk
            or AgentMessageType.AgentReply
            or AgentMessageType.AgentDialogueTurn
            or AgentMessageType.TaskAssign
            or AgentMessageType.TaskResult
            or AgentMessageType.ToolResult;
}
