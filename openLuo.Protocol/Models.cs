using System.Text.Json.Nodes;

namespace openLuo.Protocol;

/// <summary>
/// 输出项种类（对齐内核 <c>ReplyItemKind</c>）。序列化为 camelCase 字符串。
/// <see cref="Text"/>/<see cref="Card"/> 内联 <see cref="OutputDto.Payload"/>；
/// 其余（image/audio/file/asset）走 <see cref="OutputDto.AssetRef"/>。
/// </summary>
public enum OutputKind
{
    Text,
    Image,
    Audio,
    File,
    Card,
    Asset,
}

/// <summary>错误信息（信封 error 字段 / 事件 data）。</summary>
public sealed record ErrorInfo
{
    /// <summary>错误码（见 <see cref="ErrorCodes"/>）。</summary>
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>客户端是否可安全重试。</summary>
    public bool Retryable { get; init; }

    /// <summary>结构化补充信息（可选）。</summary>
    public JsonNode? Details { get; init; }
}

/// <summary>会话（wire 镜像内核 <c>AgentSession</c>）。</summary>
public sealed record SessionDto
{
    public string SessionId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string? ClientType { get; init; }
    public string? ClientId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>决策预算覆盖（wire 镜像内核 <c>DecisionBudgets</c>；null 字段用服务端默认）。</summary>
public sealed record DecisionBudgetsDto
{
    public int? MaxDecisions { get; init; }
    public int? MaxToolCallsPerDecision { get; init; }
    public int? MaxConcurrentTools { get; init; }
    public double? OverallDeadlineSec { get; init; }
    public double? StepIdleTimeoutSec { get; init; }
    public int? MaxToolRetries { get; init; }
    public int? MaxSkillLoadsPerTurn { get; init; }
}

/// <summary>回合请求（wire 镜像内核 <c>TurnRequest</c>）。</summary>
public sealed record TurnRequestDto
{
    /// <summary>可缺省，由服务端生成。</summary>
    public string? TurnId { get; init; }

    /// <summary>"player" / 角色 id。</summary>
    public string? ActorId { get; init; }

    /// <summary>来源：cli|tui|gui|qq|web|hub。</summary>
    public string? SourceId { get; init; }

    public string? ChannelId { get; init; }

    /// <summary>平台层发送者显示名（群聊昵称等）。</summary>
    public string? SenderName { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>多模态块（image/audio/file 等原始结构）。</summary>
    public IReadOnlyList<JsonNode>? Blocks { get; init; }

    /// <summary>平台元数据（scene/sender/channel 等），透传到上下文 Extras。</summary>
    public IReadOnlyDictionary<string, JsonNode?>? Meta { get; init; }

    public DecisionBudgetsDto? Budgets { get; init; }

    /// <summary>幂等键：重复提交返回既有回合，不重复执行。</summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>二进制资产引用（v1：二进制一律走 assetRef，不内联）。</summary>
public sealed record AssetRefDto
{
    public string Id { get; init; } = string.Empty;
    public string Mime { get; init; } = string.Empty;
    public long Size { get; init; }

    /// <summary>sha256（十六进制），可选。</summary>
    public string? Checksum { get; init; }
}

/// <summary>公共输出项（wire 镜像内核 <c>OutputItem</c>）。</summary>
public sealed record OutputDto
{
    public string Id { get; init; } = string.Empty;

    /// <summary>会话内单调递增序号（客户端按序渲染并 ack）。</summary>
    public long Sequence { get; init; }

    public OutputKind Kind { get; init; }

    /// <summary>内联载荷：<c>text</c> 为字符串、<c>card</c> 为结构化对象；二进制类为 null。</summary>
    public JsonNode? Payload { get; init; }

    /// <summary>二进制资产引用（image/audio/file/asset）。</summary>
    public AssetRefDto? AssetRef { get; init; }

    public string? SourceCapability { get; init; }
    public string? ConversationId { get; init; }

    /// <summary>当前回合内去重指纹。</summary>
    public string? Fingerprint { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>回合结果（wire 镜像内核 <c>TurnResult</c>；不含决策 steps，steps 走事件）。</summary>
public sealed record TurnResultDto
{
    public string TurnId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? FinalText { get; init; }
    public IReadOnlyList<OutputDto> Outputs { get; init; } = [];

    /// <summary>终止原因（见 <see cref="TerminationReasons"/>）。</summary>
    public string TerminationReason { get; init; } = TerminationReasons.FinalReply;

    public string? TerminationDetail { get; init; }
    public long StateVersion { get; init; }
}

/// <summary>能力描述（wire 镜像内核 <c>CapabilityDescriptor</c>，裁剪为客户端可见字段）。</summary>
public sealed record CapabilityDto
{
    public string CanonicalId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;

    /// <summary>见 <see cref="CapabilityKinds"/>。</summary>
    public string Kind { get; init; } = CapabilityKinds.Builtin;

    public string ProviderId { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";

    /// <summary>见 <see cref="SideEffects"/>。</summary>
    public string SideEffect { get; init; } = SideEffects.Pure;

    /// <summary>见 <see cref="RiskLevels"/>。</summary>
    public string Risk { get; init; } = RiskLevels.Low;

    public bool RequiresConfirmation { get; init; }

    /// <summary>原生 tool JSON Schema（前端可据此渲染表单/确认 UI）。</summary>
    public JsonNode? InputSchema { get; init; }
}

/// <summary>角色/Agent 摘要（目录）。</summary>
public sealed record AgentSummaryDto
{
    public string AgentId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Avatar { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>上下文区域（wire 镜像内核 <c>ContextContribution</c>）。</summary>
public sealed record ContextRegionDto
{
    public string Region { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string? Source { get; init; }

    /// <summary>ok | unavailable。</summary>
    public string? Status { get; init; }
}

/// <summary>资产元数据（HEAD / 事件内联）。</summary>
public sealed record AssetMetaDto
{
    public string Id { get; init; } = string.Empty;
    public string Mime { get; init; } = string.Empty;
    public long Size { get; init; }
    public string? Checksum { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
