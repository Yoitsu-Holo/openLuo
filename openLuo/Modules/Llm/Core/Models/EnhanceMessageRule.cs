using System.Reflection;
using System.Text.Json.Serialization;

namespace openLuo.Modules.Llm.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<EnhanceMessageRule>))]
public enum EnhanceMessageRule
{
    [JsonStringEnumMemberName("CHARACTER_PROFILE")] CharacterProfile,
    [JsonStringEnumMemberName("WORLD_CONTEXT")] WorldContext,
    [JsonStringEnumMemberName("SCENE_STATE")] SceneState,
    [JsonStringEnumMemberName("GOAL_CONTEXT")] GoalContext,
    [JsonStringEnumMemberName("EXAMPLES")] Examples,
    [JsonStringEnumMemberName("PLAYER_INPUT")] PlayerInput,
    [JsonStringEnumMemberName("RUNTIME_CONTEXT")] RuntimeContext,
    [JsonStringEnumMemberName("RESOURCE_CONTEXT")] ResourceContext,
    [JsonStringEnumMemberName("LONG_TERM_MEMORY")] LongTermMemory,
    [JsonStringEnumMemberName("KNOWN_CHARACTERS")] KnownCharacters,
    [JsonStringEnumMemberName("TOOL_CATALOG")] ToolCatalog,
    [JsonStringEnumMemberName("PRELOADED_SKILLS")] PreloadedSkills,
    [JsonStringEnumMemberName("SAFETY_OR_RUNTIME_RULES")] SafetyOrRuntimeRules
}

public static class EnhanceMessageRuleExtensions
{
    public static string ToProtocolString(this EnhanceMessageRule rule)
    {
        var member = typeof(EnhanceMessageRule)
            .GetMember(rule.ToString(), BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault();
        var attr = member?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
        return attr?.Name ?? rule.ToString();
    }
}
