using System.Text.Json.Serialization;

namespace openLuo.Modules.Executor.Application.FlowRouting;

public sealed class FlowRoutingInput
{
    public string? SystemPromptOverride { get; init; }
    public string FlowId { get; init; } = string.Empty;
    public string CurrentNodeId { get; init; } = string.Empty;
    public string PreviousNodeOutput { get; init; } = string.Empty;
    public string FlowStateSummary { get; init; } = string.Empty;
    public IReadOnlyList<FlowRoutingCandidate> Candidates { get; init; } = [];
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class FlowRoutingCandidate
{
    public string EdgeId { get; init; } = string.Empty;
    public string ToNodeId { get; init; } = string.Empty;
    public string When { get; init; } = string.Empty;
    public int Priority { get; init; }
}

public sealed class FlowRoutingOutput
{
    [JsonPropertyName("selectedEdgeId")]
    public string SelectedEdgeId { get; init; } = string.Empty;

    [JsonPropertyName("selectedNodeId")]
    public string SelectedNodeId { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("stopReason")]
    public string StopReason { get; init; } = string.Empty;
}
