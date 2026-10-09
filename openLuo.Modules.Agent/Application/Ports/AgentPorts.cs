using openLuo.Core.Models;

namespace openLuo.Modules.Agent.Application;

/// <summary>角色 roster：新链路基于 archetype 数据的最小实现契约。</summary>
public interface IAgentRoster
{
    Task<IReadOnlyList<Character>> ListAsync(string gameId, CancellationToken ct = default);

    Task<Character?> ResolveAsync(string gameId, string selector, CancellationToken ct = default);
}

/// <summary>角色间消息类型。</summary>
public enum AgentMessageType
{
    Chat,
    AgentAsk,
    AgentReply,
    System
}

/// <summary>角色间消息（最小字段集，party 扩展消费）。</summary>
public sealed record AgentMessage(
    string MessageId,
    string GameId,
    string From,
    string To,
    AgentMessageType Type,
    string Payload,
    string? CorrelationId = null,
    DateTimeOffset TimestampUtc = default);
