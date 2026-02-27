using System.Text.Json.Nodes;

namespace openLuo.Modules.WorldState.Core.Models;

public sealed class ResourceDefinitionQuery
{
    public string? Namespace { get; init; }
    public StateOwnerKind? OwnerKind { get; init; }
    public string? PluginId { get; init; }
    public ResourceLifecycleState? LifecycleState { get; init; }
    public bool VisibleInStatusOnly { get; init; }
    public bool MutableByLlmOnly { get; init; }
    public bool IncludeRetired { get; init; }
}

public sealed class ResourceDefinitionView
{
    public string DefinitionId { get; init; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public StateOwnerKind OwnerKind { get; init; } = StateOwnerKind.Game;
    public StateValueType ValueType { get; init; } = StateValueType.Number;
    public string? DefaultValue { get; init; }
    public string? MinValue { get; init; }
    public string? MaxValue { get; init; }
    public bool Derived { get; init; }
    public bool MutableByLlm { get; init; }
    public bool HiddenFromStatus { get; init; }
    public string? StatusGroup { get; init; }
    public int StatusOrder { get; init; }
    public string? DisplayFormat { get; init; }
    public string? PromptContext { get; init; }
    public string? PluginId { get; init; }
    public ResourceLifecycleState LifecycleState { get; init; } = ResourceLifecycleState.Active;
    public ResourceRetirementPolicy RetirementPolicy { get; init; } = ResourceRetirementPolicy.KeepValue;
    public string? SourceKind { get; init; }
    public string? SourceRef { get; init; }
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> EnumValues { get; init; } = [];
    public JsonObject? Metadata { get; init; }
}

public sealed class ResourceLifecycleUpdate
{
    public required string DefinitionId { get; init; }
    public ResourceLifecycleState LifecycleState { get; init; } = ResourceLifecycleState.Active;
    public ResourceRetirementPolicy RetirementPolicy { get; init; } = ResourceRetirementPolicy.KeepValue;
}

public sealed class ResourceEvaluationQuery
{
    public required string GameId { get; init; }
    public required string CharacterId { get; init; }
    public string? ArchetypeId { get; init; }
    public bool IncludeReadOnlyContext { get; init; } = true;
}

public sealed class ResourceEvaluationSnapshot
{
    public required string GameId { get; init; }
    public required string CharacterId { get; init; }
    public string? ArchetypeId { get; init; }
    public IReadOnlyList<ResourceEvaluationItemView> Items { get; init; } = [];
    public Dictionary<string, Dictionary<string, string>> StateSnapshot { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ResourceEvaluationItemView
{
    public required ResourceDefinitionView Definition { get; init; }
    public required string OwnerId { get; init; }
    public required string ResourceId { get; init; }
    public required string Value { get; init; }
    public bool Defaulted { get; init; }
    public double? MaxDeltaPerTurn { get; init; }
    public bool CanEvaluate =>
        Definition is { MutableByLlm: true, Derived: false } &&
        Definition.LifecycleState is ResourceLifecycleState.Active or ResourceLifecycleState.Hidden;
}

public sealed class ResourceValueQuery
{
    public required string GameId { get; init; }
    public string? Namespace { get; init; }
    public StateOwnerKind? OwnerKind { get; init; }
    public string? OwnerId { get; init; }
    public IReadOnlyList<string>? Keys { get; init; }
    public bool IncludeDefaults { get; init; }
}

public sealed class ResourceValueView
{
    public required ResourceDefinitionView Definition { get; init; }
    public required string GameId { get; init; }
    public required string OwnerId { get; init; }
    public required string Value { get; init; }
    public bool Defaulted { get; init; }
    public string? UpdatedAt { get; init; }
}

public sealed class ResourceStatusQuery
{
    public required string GameId { get; init; }
    public required string CharacterId { get; init; }
    public string? ArchetypeId { get; init; }
    public bool IncludeHidden { get; init; }
    public bool IncludePluginItems { get; init; } = true;
}

public sealed class ResourceStatusSnapshot
{
    public required string GameId { get; init; }
    public required string CharacterId { get; init; }
    public string? ArchetypeId { get; init; }
    public IReadOnlyList<ResourceStatusItemView> Items { get; init; } = [];
    public string AdditionalText { get; init; } = string.Empty;
}

public sealed class ResourceStatusItemView
{
    public string Id { get; init; } = string.Empty;
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public StateOwnerKind OwnerKind { get; init; } = StateOwnerKind.Game;
    public string OwnerId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Type { get; init; } = "text";
    public string Value { get; init; } = string.Empty;
    public string? Max { get; init; }
    public string Group { get; init; } = "status";
    public int Order { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? PluginId { get; init; }
    public bool Defaulted { get; init; }
    public string? UpdatedAt { get; init; }
    public bool HiddenFromStatus { get; init; }
    public bool FromPluginHook { get; init; }
}
