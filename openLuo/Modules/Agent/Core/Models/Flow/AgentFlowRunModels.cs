using System.Text.Json.Serialization;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces.Flow;

namespace openLuo.Modules.Agent.Core.Models.Flow;

public sealed class AgentFlowRunRequest
{
    [JsonPropertyName("flowId")]
    public string FlowId { get; init; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; init; } = string.Empty;

    [JsonPropertyName("gameId")]
    public string GameId { get; init; } = string.Empty;

    [JsonPropertyName("turnId")]
    public string TurnId { get; init; } = string.Empty;

    public AgentExecutionContext? ExecutionContext { get; init; }

    [JsonIgnore]
    public ITurnMessageEmitter? MessageEmitter { get; init; }

    [JsonPropertyName("inputs")]
    public IReadOnlyDictionary<string, object?> Inputs { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("maxStepsOverride")]
    public int? MaxStepsOverride { get; init; }

    public string? SessionId { get; init; }

    public string? ChannelId { get; init; }
}

public sealed class AgentFlowRunResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("terminalNodeId")]
    public string TerminalNodeId { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("outputs")]
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("steps")]
    public IReadOnlyList<AgentFlowStepResult> Steps { get; init; } = [];

    public static AgentFlowRunResult Ok(
        string terminalNodeId,
        IReadOnlyDictionary<string, object?> outputs,
        IReadOnlyList<AgentFlowStepResult> steps) => new()
    {
        Success = true,
        TerminalNodeId = terminalNodeId,
        Outputs = outputs,
        Steps = steps
    };

    public static AgentFlowRunResult Fail(
        string error,
        IReadOnlyDictionary<string, object?> outputs,
        IReadOnlyList<AgentFlowStepResult> steps) => new()
    {
        Success = false,
        Error = error,
        Outputs = outputs,
        Steps = steps
    };
}

public sealed class AgentFlowStepResult
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("nodeKind")]
    public AgentFlowNodeKind NodeKind { get; init; } = AgentFlowNodeKind.Executor;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("outputKey")]
    public string OutputKey { get; init; } = string.Empty;

    [JsonPropertyName("output")]
    public object? Output { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("nextNodeId")]
    public string NextNodeId { get; set; } = string.Empty;

    [JsonPropertyName("selectedEdgeId")]
    public string SelectedEdgeId { get; set; } = string.Empty;
}

public sealed class AgentFlowNodeExecutionResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string Error { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> StateUpdates { get; init; } = new Dictionary<string, object?>();

    public static AgentFlowNodeExecutionResult Ok(object? output, IReadOnlyDictionary<string, object?>? stateUpdates = null) => new()
    {
        Success = true,
        Output = output,
        StateUpdates = stateUpdates ?? new Dictionary<string, object?>()
    };

    public static AgentFlowNodeExecutionResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}
