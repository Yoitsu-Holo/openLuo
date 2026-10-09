using System.Text.Json.Nodes;

namespace openLuo.Protocol;

/// <summary>`POST /v1/auth/token` 请求。</summary>
public sealed record TokenRequest
{
    public string ClientId { get; init; } = string.Empty;
    public string? Secret { get; init; }
    public string? ApiKey { get; init; }
}

/// <summary>`POST /v1/auth/token` 响应。</summary>
public sealed record TokenResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>admin | user | edge。</summary>
    public string Role { get; init; } = string.Empty;
}

/// <summary>`GET /v1/health` 响应。</summary>
public sealed record HealthDto
{
    public string Status { get; init; } = "ok";
    public double UptimeSec { get; init; }
    public int McpHealthy { get; init; }
    public int McpTotal { get; init; }
    public int ExtensionsLoaded { get; init; }
}

/// <summary>`GET /v1/version` 响应。</summary>
public sealed record VersionDto
{
    public string ServerVersion { get; init; } = string.Empty;
    public int ProtocolVersion { get; init; } = ProtocolInfo.MajorVersion;
    public IReadOnlyList<string> Features { get; init; } = [];
}

/// <summary>`POST /v1/sessions` 请求。</summary>
public sealed record CreateSessionRequest
{
    public string SubjectId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string ClientType { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
    public IReadOnlyDictionary<string, JsonNode?>? Meta { get; init; }
}

/// <summary>`GET /v1/agents` 响应。</summary>
public sealed record AgentsResponse
{
    public IReadOnlyList<AgentSummaryDto> Agents { get; init; } = [];
}

/// <summary>`GET /v1/capabilities` 响应。</summary>
public sealed record CapabilitiesResponse
{
    public long Version { get; init; }
    public IReadOnlyList<CapabilityDto> Capabilities { get; init; } = [];
}

/// <summary>`POST /v1/sessions/{id}/turns` 响应（202）。</summary>
public sealed record TurnAcceptedResponse
{
    public string TurnId { get; init; } = string.Empty;
    public bool Accepted { get; init; } = true;
}

/// <summary>`GET /v1/turns/{turnId}` 响应（无 WS 客户端的轮询 fallback）。</summary>
public sealed record TurnStatusResponse
{
    public string TurnId { get; init; } = string.Empty;

    /// <summary>running | done | failed | cancelled。</summary>
    public string Status { get; init; } = string.Empty;

    public TurnResultDto? Result { get; init; }
    public IReadOnlyList<OutputDto> Outputs { get; init; } = [];
}

/// <summary>`POST /v1/sessions/{id}/messages` 响应。</summary>
public sealed record AppendMessageResponse
{
    public bool Appended { get; init; } = true;
}

/// <summary>`GET /v1/sessions/{id}/context` 响应。</summary>
public sealed record ContextResponse
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<ContextRegionDto> Regions { get; init; } = [];
}

/// <summary>`GET /v1/sessions/{id}/state` 响应（世界状态只读投影）。</summary>
public sealed record StateResponse
{
    public long Version { get; init; }
    public JsonNode? Values { get; init; }
}
