namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>Type of state value.</summary>
public enum StateValueType { Number, Enum, Text, Json, AssetRef }

/// <summary>Kind of entity that owns a state value.</summary>
public enum StateOwnerKind { Game, Character, Scene, Object, System }

/// <summary>Runtime lifecycle policy for a registered state definition.</summary>
public enum ResourceLifecycleState { Active, Hidden, Frozen, Retired }

/// <summary>How persisted values should be treated when a resource is retired.</summary>
public enum ResourceRetirementPolicy { KeepValue, HideValue, PurgeValue }

/// <summary>
/// Definition of a state field registered by a plugin.
/// Analogous to a schema entry — describes what values are valid and how they behave.
/// </summary>
public class StateDef
{
    /// <summary>Business domain namespace (e.g., "char_status", "world_state", "game_resource").</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Field name within the namespace (e.g., "affection", "mood", "gold").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Kind of owner entity this state belongs to.</summary>
    public StateOwnerKind OwnerKind { get; set; } = StateOwnerKind.Game;

    /// <summary>Value type for this state.</summary>
    public StateValueType ValueType { get; set; } = StateValueType.Number;

    /// <summary>Default value as string.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Minimum value (numeric types only).</summary>
    public string? MinValue { get; set; }

    /// <summary>Maximum value (numeric types only).</summary>
    public string? MaxValue { get; set; }

    /// <summary>Whether this state is computed from other states; not writable by plugins directly.</summary>
    public bool Derived { get; set; } = false;

    /// <summary>Whether the LLM evaluator can modify this state.</summary>
    public bool MutableByLlm { get; set; } = false;

    /// <summary>Display group for status UI (e.g., "intimacy", "resources").</summary>
    public string? StatusGroup { get; set; }

    /// <summary>Display order within status group.</summary>
    public int StatusOrder { get; set; } = 0;

    /// <summary>Hide from status display (still readable/writable).</summary>
    public bool HiddenFromStatus { get; set; } = false;

    /// <summary>Display format string (e.g., "好感：{value}/1000").</summary>
    public string? DisplayFormat { get; set; }

    /// <summary>Semantic description for LLM prompt context.</summary>
    public string? PromptContext { get; set; }

    /// <summary>Plugin that registered this definition.</summary>
    public string? PluginId { get; set; }

    /// <summary>Extra plugin-specific metadata (JSON string).</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Valid enum values (for StateValueType.Enum).</summary>
    public List<string> EnumValues { get; set; } = [];

    /// <summary>Whether the resource participates in runtime projections and mutation flows.</summary>
    public ResourceLifecycleState LifecycleState { get; set; } = ResourceLifecycleState.Active;

    /// <summary>Value retention policy when the resource is retired.</summary>
    public ResourceRetirementPolicy RetirementPolicy { get; set; } = ResourceRetirementPolicy.KeepValue;

    /// <summary>Where this definition originated: core/plugin/content_pack/etc.</summary>
    public string? SourceKind { get; set; }

    /// <summary>Origin identifier, usually plugin id or content pack id.</summary>
    public string? SourceRef { get; set; }

    /// <summary>Computed definition ID: "namespace:ownerKind:key".</summary>
    public string DefinitionId => $"{Namespace}:{OwnerKind.ToString().ToLowerInvariant()}:{Key}";
}
