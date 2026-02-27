using System.Text.Json.Serialization;

namespace openLuo.Modules.Content.Core.Definitions;

public sealed class CharacterArchetypeDefinition : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.CharacterArchetype;

    [JsonPropertyName("name")]
    public string LegacyName
    {
        get => DisplayName;
        set => DisplayName = value ?? string.Empty;
    }

    public string CharacterName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Backstory { get; set; } = string.Empty;
    public string InitialLocation { get; set; } = string.Empty;
    public string SocialStyle { get; set; } = "balanced";
    public string OutingInitiativeFrom { get; set; } = "friend";
    public bool MoodAffectsInitiative { get; set; } = true;
    public List<string> Traits { get; set; } = [];
    public List<string> Likes { get; set; } = [];
    public List<string> Dislikes { get; set; } = [];
    public List<string> Habits { get; set; } = [];
    public Dictionary<string, string> NarrativeHints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> EmotionalTriggers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Goals { get; set; } = [];

    [JsonPropertyName("basePrompt")]
    public string LegacyBasePrompt
    {
        get => Prompt;
        set => Prompt = value ?? string.Empty;
    }

    [JsonPropertyName("systemPrompt")]
    public string LegacySystemPrompt
    {
        get => Prompt;
        set => Prompt = value ?? string.Empty;
    }

    [JsonPropertyName("agentGoals")]
    public List<string> LegacyAgentGoals
    {
        get => Goals;
        set => Goals = value ?? [];
    }

    [JsonPropertyName("personality")]
    public CharacterArchetypePersonality? LegacyPersonality
    {
        get => new()
        {
            SocialStyle = SocialStyle,
            OutingInitiativeFrom = OutingInitiativeFrom,
            MoodAffectsInitiative = MoodAffectsInitiative,
            Traits = [.. Traits]
        };
        set
        {
            if (value is null)
                return;

            SocialStyle = value.SocialStyle;
            OutingInitiativeFrom = value.OutingInitiativeFrom;
            MoodAffectsInitiative = value.MoodAffectsInitiative;
            Traits = [.. value.Traits];
        }
    }
}

public sealed class CharacterArchetypePersonality
{
    public string SocialStyle { get; set; } = "balanced";
    public string OutingInitiativeFrom { get; set; } = "friend";
    public bool MoodAffectsInitiative { get; set; } = true;
    public List<string> Traits { get; set; } = [];
}
