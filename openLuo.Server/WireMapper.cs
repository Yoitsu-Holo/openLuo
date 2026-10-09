using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>内核模型 → Wire DTO 的映射（Hub 唯一负责；内核不感知协议）。</summary>
public static class WireMapper
{
    public static OutputDto ToDto(OutputItem item) => new()
    {
        Id = item.Id,
        Sequence = item.Sequence,
        Kind = ToKind(item.Kind),
        Payload = ToPayload(item.Payload),
        // MVP：二进制仍以内联（data URL）承载；assetRef 迁移见 §9（后续）。
        AssetRef = null,
        SourceCapability = Blank(item.SourceCapability),
        ConversationId = Blank(item.ConversationId),
        Fingerprint = Blank(item.Fingerprint),
        CreatedAt = item.CreatedAtUtc,
    };

    public static TurnResultDto ToDto(TurnResult result, string turnId) => new()
    {
        TurnId = turnId,
        Success = result.Success,
        FinalText = result.FinalText,
        Outputs = result.Outputs.Select(ToDto).ToList(),
        TerminationReason = ToTermination(result.TerminationReason),
        TerminationDetail = result.TerminationDetail,
        StateVersion = result.StateVersion,
    };

    public static SessionDto ToDto(AgentSession session, string? clientType = null, string? clientId = null) => new()
    {
        SessionId = session.SessionId,
        SubjectId = session.SubjectId,
        AgentId = session.AgentId,
        ConversationId = session.ConversationId,
        ClientType = clientType,
        ClientId = clientId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static DecisionStepEvent ToDecision(string turnId, int step, string? note = null) => new()
    {
        TurnId = turnId,
        Step = step,
        Note = note,
    };

    public static CapabilityDto ToDto(CapabilityDescriptor descriptor) => new()
    {
        CanonicalId = descriptor.CanonicalId,
        DisplayName = descriptor.DisplayName,
        Summary = descriptor.Summary,
        Usage = descriptor.Usage,
        Kind = descriptor.Kind switch
        {
            CapabilityKind.Mcp => CapabilityKinds.Mcp,
            CapabilityKind.Workflow => CapabilityKinds.Workflow,
            CapabilityKind.RemoteAgent => CapabilityKinds.RemoteAgent,
            _ => CapabilityKinds.Builtin,
        },
        ProviderId = descriptor.ProviderId,
        Version = descriptor.Version,
        SideEffect = descriptor.SideEffect switch
        {
            SideEffectClass.ReadOnly => SideEffects.ReadOnly,
            SideEffectClass.External => SideEffects.External,
            SideEffectClass.Mutation => SideEffects.Mutation,
            SideEffectClass.Delegation => SideEffects.Delegation,
            _ => SideEffects.Pure,
        },
        Risk = descriptor.Risk switch
        {
            RiskLevel.Medium => RiskLevels.Medium,
            RiskLevel.High => RiskLevels.High,
            _ => RiskLevels.Low,
        },
        RequiresConfirmation = descriptor.RequiresConfirmation,
        InputSchema = JsonSerializer.SerializeToNode(descriptor.InputSchema, ProtocolJson.Options),
    };

    public static ToolCallEvent ToToolCall(string turnId, CapabilityCall call) => new()
    {
        TurnId = turnId,
        CallId = call.InvocationId,
        CanonicalId = call.CanonicalId,
        Arguments = string.IsNullOrWhiteSpace(call.RawArgumentsJson)
            ? null
            : TryParse(call.RawArgumentsJson),
    };

    public static ToolResultEvent ToToolResult(string turnId, CapabilityResult result) => new()
    {
        TurnId = turnId,
        CallId = result.InvocationId,
        Status = result.Success ? "ok" : result.Status.ToString().ToLowerInvariant(),
        Preview = Blank(result.Text ?? result.Error),
    };

    private static OutputKind ToKind(ReplyItemKind kind) => kind switch
    {
        ReplyItemKind.Text => OutputKind.Text,
        ReplyItemKind.Image => OutputKind.Image,
        ReplyItemKind.Audio => OutputKind.Audio,
        ReplyItemKind.File => OutputKind.File,
        ReplyItemKind.Card => OutputKind.Card,
        ReplyItemKind.Asset => OutputKind.Asset,
        _ => OutputKind.Text,
    };

    private static string ToTermination(TerminationReason reason) => reason switch
    {
        TerminationReason.FinalReply => TerminationReasons.FinalReply,
        TerminationReason.MaxDecisionsReached => TerminationReasons.MaxDecisionsReached,
        TerminationReason.OverallTimeout => TerminationReasons.OverallTimeout,
        TerminationReason.TerminalCapability => TerminationReasons.TerminalCapability,
        TerminationReason.NoProgress => TerminationReasons.NoProgress,
        TerminationReason.Cancelled => TerminationReasons.Cancelled,
        TerminationReason.EmptyReply => TerminationReasons.EmptyReply,
        _ => TerminationReasons.FinalReply,
    };

    private static JsonNode? ToPayload(object? payload) => payload switch
    {
        null => null,
        string text => JsonValue.Create(text),
        JsonNode node => node,
        _ => JsonSerializer.SerializeToNode(payload, ProtocolJson.Options),
    };

    private static JsonNode? TryParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch { return JsonValue.Create(json); }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
