using openLuo.Capabilities.Core;
using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.Agent.Application.Runtime;

/// <summary>
/// 角色间通信枢纽：基于新链路 IAgentRuntime 实现。
/// RequestAsync 以目标角色为 subject 打开/复用会话并跑一个回合，返回其回复。
/// </summary>
public interface IAgentRuntimeHub
{
    Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

public sealed class AgentRuntimeHub : IAgentRuntimeHub, IAsyncDisposable
{
    private readonly Func<IAgentRuntime> _runtimeFactory;

    // 懒解析：组合根中 IAgentRuntime 依赖 dispatcher（读扩展注册表），
    // 而 party 扩展构造注入本 Hub 早于注册表填充——急切解析会以空注册表
    // 构建 dispatcher（canonicalInvokers 空 → 所有能力 Unbound）。
    public AgentRuntimeHub(Func<IAgentRuntime> runtimeFactory) => _runtimeFactory = runtimeFactory;

    public async Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var runtime = _runtimeFactory();

        var sessionId = $"party-{gameId}-{characterId}";
        await runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = sessionId,
            SubjectId = characterId,
            AgentId = "companion",
            ClientType = "party",
            ClientId = gameId,
            ConversationId = sessionId
        }, ct);
        var result = await runtime.RunTurnAsync(new TurnRequest
        {
            SessionId = sessionId,
            TurnId = Guid.NewGuid().ToString("N"),
            SourceId = "party",
            ChannelId = gameId,
            ActorId = from,
            Text = payload
        }, ct);
        return new AgentMessage(
            Guid.NewGuid().ToString("N"),
            gameId,
            characterId,
            from,
            AgentMessageType.AgentReply,
            result.FinalText ?? string.Empty,
            correlationId,
            DateTimeOffset.UtcNow);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
