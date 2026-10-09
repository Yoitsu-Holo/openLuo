using System.Text.Json.Nodes;

namespace openLuo.Protocol;

/// <summary>`notification`：非回合绑定的服务端通知（提醒 / 告警 / 主动搭话提示）。</summary>
public sealed record NotificationEvent
{
    /// <summary>通知种类（自由字符串，如 reminder | alert | idle | device）。</summary>
    public string Kind { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string? Body { get; init; }
    public string? SessionId { get; init; }
    public JsonNode? Data { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>`turn.started`：服务端发起的回合（非客户端 `turn.submit`）。</summary>
public sealed record TurnStartedEvent
{
    public string TurnId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;

    /// <summary>见 <see cref="TurnOrigins"/>。</summary>
    public string Origin { get; init; } = TurnOrigins.Scheduled;

    /// <summary>触发细节（如 scheduleId / deviceId / presence userId）。</summary>
    public string? Trigger { get; init; }
}

/// <summary>调度条目（定时 / 条件触发）。</summary>
public sealed record ScheduleDto
{
    public string Id { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;

    /// <summary>种类（自由字符串，如 daily | once | idle | device）。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>cron 表达式（周期触发）或 null。</summary>
    public string? Cron { get; init; }

    /// <summary>一次性触发时间（once）。</summary>
    public DateTimeOffset? At { get; init; }

    public bool Enabled { get; init; } = true;
    public DateTimeOffset? NextRunAt { get; init; }
    public JsonNode? Payload { get; init; }
}

/// <summary>设备（智能家居）状态。</summary>
public sealed record DeviceStateDto
{
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>设备种类（自由字符串，如 light | lock | sensor）。</summary>
    public string? Kind { get; init; }

    /// <summary>设备状态载荷（开关 / 数值 / 枚举等）。</summary>
    public JsonNode? State { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>`device.report`：边缘 / 网关上报设备状态。</summary>
public sealed record DeviceReportCommand
{
    public string DeviceId { get; init; } = string.Empty;
    public string? Kind { get; init; }
    public JsonNode? State { get; init; }
}

/// <summary>`device.state`：设备状态广播（服务端）。</summary>
public sealed record DeviceStateEvent
{
    public DeviceStateDto Device { get; init; } = new();
}
