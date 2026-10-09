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

// ─────────────────────────── 配置（管理，需 admin 权限） ───────────────────────────

/// <summary>`GET /v1/config` 单项（命名空间摘要）。</summary>
public sealed record ConfigNamespaceDto
{
    /// <summary>命名空间（对应 <c>config/{ns}.jsonc</c>），如 llm / agent / timeouts。</summary>
    public string Namespace { get; init; } = string.Empty;

    /// <summary>有效值来源：见 <see cref="ConfigSources"/>。</summary>
    public string Source { get; init; } = ConfigSources.Default;

    /// <summary>是否存在覆盖（file 或 runtime）。</summary>
    public bool Overridden { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>`GET /v1/config` 响应。</summary>
public sealed record ConfigListResponse
{
    public IReadOnlyList<ConfigNamespaceDto> Namespaces { get; init; } = [];
}

/// <summary>`GET /v1/config/{ns}` 响应（敏感字段已掩码为 <c>***</c>）。</summary>
public sealed record ConfigGetResponse
{
    public string Namespace { get; init; } = string.Empty;

    /// <summary>见 <see cref="ConfigSources"/>。</summary>
    public string Source { get; init; } = ConfigSources.Default;

    /// <summary>合并后的有效值。</summary>
    public JsonNode? Values { get; init; }

    /// <summary>当前覆盖层（file ⊕ runtime）；无覆盖为 null。</summary>
    public JsonNode? Overrides { get; init; }
}

/// <summary>`POST /v1/config/{ns}` 请求：以 JSON 合并方式写入覆盖层。</summary>
public sealed record ConfigSetRequest
{
    /// <summary>待合并的配置片段（对象）。</summary>
    public JsonNode? Values { get; init; }

    /// <summary>true = 写回 <c>config/{ns}.jsonc</c>；false = 仅运行时（重启丢失）。</summary>
    public bool Persist { get; init; }
}

/// <summary>`POST /v1/config/{ns}` 响应（返回合并后的有效值）。</summary>
public sealed record ConfigSetResponse
{
    public string Namespace { get; init; } = string.Empty;

    /// <summary>见 <see cref="ConfigSources"/>。</summary>
    public string Source { get; init; } = ConfigSources.Runtime;

    public JsonNode? Values { get; init; }
}

/// <summary>`DELETE /v1/config/{ns}` 请求：删除覆盖，回退到下一层（最终为默认值）。</summary>
public sealed record ConfigDeleteRequest
{
    /// <summary>true = 同时删除 <c>config/{ns}.jsonc</c>；false = 仅清运行时覆盖。</summary>
    public bool Persist { get; init; }
}

/// <summary>`DELETE /v1/config/{ns}` 响应。</summary>
public sealed record ConfigDeleteResponse
{
    public string Namespace { get; init; } = string.Empty;

    /// <summary>删除后回退到的来源（通常为 <see cref="ConfigSources.Default"/>）。</summary>
    public string Source { get; init; } = ConfigSources.Default;

    public JsonNode? Values { get; init; }
}
