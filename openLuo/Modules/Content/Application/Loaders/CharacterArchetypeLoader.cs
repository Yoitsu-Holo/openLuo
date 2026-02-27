using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Loaders;

public static class CharacterArchetypeLoader
{
    public static IReadOnlyList<CharacterArchetypeDefinition> LoadAll(string baseDir, IGameStreams? streams = null)
    {
        var results = new List<CharacterArchetypeDefinition>();
        var dir = Path.Combine(baseDir, "data", "archetypes");
        if (!Directory.Exists(dir)) return results;

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonc", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var definition = ParseDefinition(File.ReadAllText(file));
                if (definition is not null && !string.IsNullOrWhiteSpace(definition.Id))
                    results.Add(definition);
            }
            catch (Exception ex)
            {
                if (streams is null)
                    continue;

                var msg = $"[WARN] Failed to load archetype {Path.GetFileName(file)}: {ex.Message}\n";
                streams.Error.Write(Encoding.UTF8.GetBytes(msg));
            }
        }

        return results;
    }

    public static PackManifest LoadPack(IReadOnlyList<CharacterArchetypeDefinition> definitions)
    {
        return new PackManifest
        {
            Id = "builtin.character_archetypes",
            DisplayName = "Builtin Character Archetypes",
            Description = "Character archetypes loaded from data/archetypes.",
            Contents = definitions
                .Where(definition => !string.IsNullOrWhiteSpace(definition.Id))
                .Select(definition => new PackContentRef
                {
                    Kind = ContentKind.CharacterArchetype,
                    Id = definition.Id
                })
                .ToList()
        };
    }

    private static CharacterArchetypeDefinition? ParseDefinition(string json)
    {
        var root = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject;
        if (root is null) return null;

        var personality = root["personality"] as JsonObject;
        return new CharacterArchetypeDefinition
        {
            Id = root["id"]?.GetValue<string>() ?? string.Empty,
            DisplayName = root["displayName"]?.GetValue<string>()
                ?? root["name"]?.GetValue<string>()
                ?? string.Empty,
            CharacterName = root["characterName"]?.GetValue<string>() ?? string.Empty,
            Description = root["description"]?.GetValue<string>() ?? string.Empty,
            Prompt = root["prompt"]?.GetValue<string>()
                ?? root["basePrompt"]?.GetValue<string>()
                ?? root["systemPrompt"]?.GetValue<string>()
                ?? string.Empty,
            Backstory = root["backstory"]?.GetValue<string>() ?? string.Empty,
            InitialLocation = root["initialLocation"]?.GetValue<string>() ?? string.Empty,
            SocialStyle = root["socialStyle"]?.GetValue<string>()
                ?? personality?["socialStyle"]?.GetValue<string>()
                ?? "balanced",
            OutingInitiativeFrom = root["outingInitiativeFrom"]?.GetValue<string>()
                ?? personality?["outingInitiativeFrom"]?.GetValue<string>()
                ?? "friend",
            MoodAffectsInitiative = root["moodAffectsInitiative"]?.GetValue<bool?>()
                ?? personality?["moodAffectsInitiative"]?.GetValue<bool?>()
                ?? true,
            Traits = ReadStringList(root["traits"] as JsonArray ?? personality?["traits"] as JsonArray),
            Likes = ReadStringList(root["likes"] as JsonArray),
            Dislikes = ReadStringList(root["dislikes"] as JsonArray),
            Habits = ReadStringList(root["habits"] as JsonArray),
            NarrativeHints = ReadStringMap(root["narrativeHints"] as JsonObject),
            EmotionalTriggers = ReadStringListMap(root["emotionalTriggers"] as JsonObject),
            Goals = ReadStringList(root["goals"] as JsonArray ?? root["agentGoals"] as JsonArray)
        };
    }

    private static List<string> ReadStringList(JsonArray? array) =>
        array?.Select(node => node?.GetValue<string>() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList()
        ?? [];

    private static Dictionary<string, string> ReadStringMap(JsonObject? obj)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (obj is null) return result;

        foreach (var pair in obj)
        {
            if (pair.Value is null) continue;
            var value = pair.Value.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
                result[pair.Key] = value;
        }

        return result;
    }

    private static Dictionary<string, List<string>> ReadStringListMap(JsonObject? obj)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (obj is null) return result;

        foreach (var pair in obj)
        {
            if (pair.Value is not JsonArray array) continue;
            result[pair.Key] = ReadStringList(array);
        }

        return result;
    }
}
