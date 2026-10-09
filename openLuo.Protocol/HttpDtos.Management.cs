using System.Text.Json.Nodes;

namespace openLuo.Protocol;

// ─────────────────────────── 调度（管理） ───────────────────────────

/// <summary>`GET /v1/schedules` 响应。</summary>
public sealed record ScheduleListResponse
{
    public IReadOnlyList<ScheduleDto> Schedules { get; init; } = [];
}

/// <summary>`POST /v1/schedules` 请求。</summary>
public sealed record CreateScheduleRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? Cron { get; init; }
    public DateTimeOffset? At { get; init; }
    public JsonNode? Payload { get; init; }
}

// ─────────────────────────── 作业（长任务） ───────────────────────────

/// <summary>`POST /v1/jobs` 请求。</summary>
public sealed record CreateJobRequest
{
    public string Kind { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public JsonNode? Payload { get; init; }
}

/// <summary>`GET /v1/jobs/{id}` 响应。</summary>
public sealed record JobStatusResponse
{
    public JobDto Job { get; init; } = new();
}

// ─────────────────────────── 资产上传（客户端 → 服务端） ───────────────────────────

/// <summary>`POST /v1/assets` 请求元数据（本体为二进制请求体）。</summary>
public sealed record UploadAssetRequest
{
    /// <summary>期望的 MIME 类型。</summary>
    public string Mime { get; init; } = string.Empty;

    /// <summary>建议文件名（可选）。</summary>
    public string? FileName { get; init; }
}

/// <summary>`POST /v1/assets` 响应。</summary>
public sealed record UploadAssetResponse
{
    public AssetRefDto Asset { get; init; } = new();
    public AssetMetaDto Meta { get; init; } = new();
}

// ─────────────────────────── 观测 / 审计 ───────────────────────────

/// <summary>单条决策轨迹事件（回放用）。</summary>
public sealed record TraceEventDto
{
    public DateTimeOffset Ts { get; init; }

    /// <summary>事件类型，与 WS 事件同名（decision / tool.call / tool.result / output）。</summary>
    public string Type { get; init; } = string.Empty;

    public JsonNode? Data { get; init; }
}

/// <summary>`GET /v1/traces/{turnId}` 响应（观测 / 回放）。</summary>
public sealed record TurnTraceDto
{
    public string TurnId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public IReadOnlyList<TraceEventDto> Events { get; init; } = [];
}

/// <summary>`GET /v1/metrics` 响应。</summary>
public sealed record MetricsDto
{
    public double UptimeSec { get; init; }
    public int SessionsActive { get; init; }
    public long TurnsTotal { get; init; }
    public long ErrorsTotal { get; init; }
    public int ClientsConnected { get; init; }
}

/// <summary>`audit.event`：审计事件（admin 订阅）。</summary>
public sealed record AuditEventDto
{
    public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>发起方 clientId（服务端自身触发的为 null）。</summary>
    public string? ClientId { get; init; }

    /// <summary>动作（自由字符串，如 config.set | session.open | capability.invoke）。</summary>
    public string Action { get; init; } = string.Empty;

    public string? Target { get; init; }

    /// <summary>结果：ok | denied | failed。</summary>
    public string? Result { get; init; }

    public JsonNode? Details { get; init; }
}
