namespace openLuo.Protocol;

/// <summary>在线客户端（连接）摘要。</summary>
public sealed record PresenceDto
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientType { get; init; } = string.Empty;

    /// <summary>见 <see cref="PresenceStatuses"/>。</summary>
    public string Status { get; init; } = PresenceStatuses.Online;

    /// <summary>该客户端订阅的会话。</summary>
    public IReadOnlyList<string> Sessions { get; init; } = [];

    public DateTimeOffset ConnectedAt { get; init; }
}

/// <summary>`presence.subscribe` / `presence.unsubscribe`：订阅在线状态（可选限定会话）。</summary>
public sealed record PresenceSubscription
{
    public IReadOnlyList<string>? SessionIds { get; init; }
}

/// <summary>`presence.updated`：在线状态变化（增量）。</summary>
public sealed record PresenceUpdatedEvent
{
    public string ClientId { get; init; } = string.Empty;

    /// <summary>见 <see cref="PresenceStatuses"/>。</summary>
    public string Status { get; init; } = PresenceStatuses.Offline;

    /// <summary>完整快照（首次订阅 / 加入时为整条）。</summary>
    public PresenceDto? Presence { get; init; }
}

/// <summary>`session.resume`：断线重连后按 sequence 续传订阅会话的未收输出。</summary>
public sealed record SessionResumeCommand
{
    public string SessionId { get; init; } = string.Empty;

    /// <summary>客户端已收到的最后 sequence；服务端补发其后内容。</summary>
    public long SinceSequence { get; init; }
}
