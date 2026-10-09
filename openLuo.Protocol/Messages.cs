using System.Text.Json.Nodes;

namespace openLuo.Protocol;

// ─────────────────────────── 客户端 → 服务端（命令 data） ───────────────────────────

/// <summary>`hello`：WebSocket 首帧，版本协商 + 鉴权。</summary>
public sealed record HelloCommand
{
    public int ProtocolVersion { get; init; } = ProtocolInfo.MajorVersion;

    /// <summary>Bearer token；<c>server.allowAnonymous</c> 下可空。</summary>
    public string? Token { get; init; }

    public string ClientId { get; init; } = string.Empty;

    /// <summary>cli|tui|gui|qq|web|hub。</summary>
    public string ClientType { get; init; } = string.Empty;

    /// <summary>客户端支持的特性（见 <see cref="Features"/>）。</summary>
    public IReadOnlyList<string> Features { get; init; } = [];
}

/// <summary>`session.open`：开启会话。</summary>
public sealed record SessionOpenCommand
{
    public string SubjectId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
    public IReadOnlyDictionary<string, JsonNode?>? Meta { get; init; }
}

/// <summary>`session.subscribe` / `session.unsubscribe` / `session.close`：会话引用。</summary>
public sealed record SessionRef
{
    public string SessionId { get; init; } = string.Empty;
}

/// <summary>`turn.cancel`：取消进行中回合。</summary>
public sealed record TurnCancelCommand
{
    public string TurnId { get; init; } = string.Empty;
}

/// <summary>`message.append`：写入历史但不触发回合（"看但不回"）。</summary>
public sealed record MessageAppendCommand
{
    public string SessionId { get; init; } = string.Empty;
    public string? SenderName { get; init; }
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<JsonNode>? Blocks { get; init; }
    public IReadOnlyDictionary<string, JsonNode?>? Meta { get; init; }
}

/// <summary>`output.ack`：输出项已成功投递。</summary>
public sealed record OutputAckCommand
{
    public long Sequence { get; init; }
}

/// <summary>`output.fail`：输出项投递失败。</summary>
public sealed record OutputFailCommand
{
    public long Sequence { get; init; }

    /// <summary>true 表示放弃该条；false 表示可重试。</summary>
    public bool Permanent { get; init; }
}

/// <summary>`confirm.response`：高危能力确认结果。</summary>
public sealed record ConfirmResponseCommand
{
    public string RequestId { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public string? Reason { get; init; }
}

// ─────────────────────────── 服务端 → 客户端（事件 data） ───────────────────────────

/// <summary>`welcome`：协商结果。</summary>
public sealed record WelcomeEvent
{
    public int ProtocolVersion { get; init; } = ProtocolInfo.MajorVersion;
    public string ServerVersion { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;

    /// <summary>服务端与客户端特性的交集。</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    public int HeartbeatIntervalSec { get; init; } = ProtocolInfo.DefaultHeartbeatSec;
}

/// <summary>`session.closed`。</summary>
public sealed record SessionClosedEvent
{
    public string SessionId { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

/// <summary>`turn.accepted`：回合已受理。</summary>
public sealed record TurnAcceptedEvent
{
    public string TurnId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
}

/// <summary>`decision`：模型决策步骤。</summary>
public sealed record DecisionEvent
{
    public string TurnId { get; init; } = string.Empty;
    public int Step { get; init; }
    public string? ModelToolName { get; init; }
    public string? Note { get; init; }
}

/// <summary>`tool.call`：工具发起。</summary>
public sealed record ToolCallEvent
{
    public string TurnId { get; init; } = string.Empty;
    public string CallId { get; init; } = string.Empty;
    public string CanonicalId { get; init; } = string.Empty;
    public JsonNode? Arguments { get; init; }
}

/// <summary>`tool.result`：工具结果（预览，完整输出走 output 事件）。</summary>
public sealed record ToolResultEvent
{
    public string TurnId { get; init; } = string.Empty;
    public string CallId { get; init; } = string.Empty;

    /// <summary>ok | failed | rejected。</summary>
    public string Status { get; init; } = string.Empty;

    public string? Preview { get; init; }
}

/// <summary>`context.updated`：上下文快照变更（调试/可视化）。</summary>
public sealed record ContextUpdatedEvent
{
    public string SessionId { get; init; } = string.Empty;
    public IReadOnlyList<ContextRegionDto> Regions { get; init; } = [];
}

/// <summary>`state.updated`：世界状态变更。</summary>
public sealed record StateUpdatedEvent
{
    public string SessionId { get; init; } = string.Empty;
    public long Version { get; init; }
    public JsonNode? Patch { get; init; }
}

/// <summary>`confirm.request`：高危能力需确认。</summary>
public sealed record ConfirmRequestEvent
{
    public string RequestId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string CanonicalId { get; init; } = string.Empty;

    /// <summary>见 <see cref="RiskLevels"/>。</summary>
    public string Risk { get; init; } = RiskLevels.Low;

    public string Summary { get; init; } = string.Empty;
    public JsonNode? ArgsPreview { get; init; }
}

/// <summary>`pong`：心跳应答。</summary>
public sealed record PongEvent
{
    public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;
}
