namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>One schedulable timeline event.</summary>
public sealed class TimelineEvent
{
    public string Id { get; set; } = string.Empty;

    public string GameId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public long DueAtEpochMs { get; set; }

    public long? EndAtEpochMs { get; set; }

    public string? RecurrenceRule { get; set; }

    public string Status { get; set; } = TimelineEventStatus.Pending;

    public string? ActionJson { get; set; }

    public string? ContextJson { get; set; }

    public string CreatedAt { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;
}

public static class TimelineEventStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Done = "done";
    public const string Missed = "missed";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status) => status is Done or Missed or Cancelled;
}

public sealed class TimelineCreateRequest
{
    public string EventType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public long DueAtEpochMs { get; set; }

    public long? EndAtEpochMs { get; set; }

    public string? RecurrenceRule { get; set; }

    public string? ActionJson { get; set; }

    public string? ContextJson { get; set; }
}

public sealed class TimelineQueryOptions
{
    public string? EventType { get; set; }

    public string? Status { get; set; }

    public int Limit { get; set; } = 50;

    public int Offset { get; set; } = 0;
}
