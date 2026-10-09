namespace openLuo.Protocol;

/// <summary>作业（长任务）描述。</summary>
public sealed record JobDto
{
    public string Id { get; init; } = string.Empty;

    /// <summary>作业种类（自由字符串，如 cg.generate | tts.batch | live2d.build）。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>见 <see cref="JobStatuses"/>。</summary>
    public string Status { get; init; } = JobStatuses.Queued;

    /// <summary>进度 0..1。</summary>
    public double Progress { get; init; }

    public string? Message { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlyList<OutputDto> Outputs { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>`job.accepted`：作业已受理。</summary>
public sealed record JobAcceptedEvent
{
    public string JobId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? SessionId { get; init; }
}

/// <summary>`job.progress`：作业进度。</summary>
public sealed record JobProgressEvent
{
    public string JobId { get; init; } = string.Empty;
    public double Progress { get; init; }
    public string? Message { get; init; }
}

/// <summary>`job.completed`：作业完成（含产出）。</summary>
public sealed record JobCompletedEvent
{
    public string JobId { get; init; } = string.Empty;
    public IReadOnlyList<OutputDto> Outputs { get; init; } = [];
}

/// <summary>`job.failed`：作业失败。</summary>
public sealed record JobFailedEvent
{
    public string JobId { get; init; } = string.Empty;
    public int ErrorCode { get; init; } = ErrorCodes.JobFailed;
    public string ErrorMsg { get; init; } = string.Empty;
}
