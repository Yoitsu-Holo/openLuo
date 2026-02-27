namespace openLuo.Modules.Agent.Core.Models;

public sealed class PartyTaskRecord
{
    public string Id { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string ContextJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PartyTaskStepRecord
{
    public string Id { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public string AssignedCharacterId { get; set; } = string.Empty;
    public string Role { get; set; } = "support";
    public string Instruction { get; set; } = string.Empty;
    public string ResultJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
