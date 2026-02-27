using System.Text.Json.Nodes;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Registry;

public sealed class ContentRegistry
{
    private readonly IReadOnlyDictionary<ContentKind, IReadOnlyDictionary<string, RegistryEntry>> _entries;

    public ContentRegistry(IReadOnlyDictionary<ContentKind, IReadOnlyDictionary<string, RegistryEntry>> entries)
    {
        _entries = entries;
    }

    public IReadOnlyCollection<RegistryEntry> Entries =>
        _entries.Values.SelectMany(x => x.Values).ToArray();

    public IReadOnlyList<TDefinition> GetAll<TDefinition>() where TDefinition : ContentDefinitionBase =>
        Entries
            .Select(entry => entry.Definition)
            .OfType<TDefinition>()
            .ToArray();

    public bool TryGet<TDefinition>(string id, out TDefinition? definition) where TDefinition : ContentDefinitionBase
    {
        var kind = ResolveKind<TDefinition>();
        definition = null;

        if (_entries.TryGetValue(kind, out var byId) &&
            byId.TryGetValue(id.Trim(), out var entry) &&
            entry.Definition is TDefinition typed)
        {
            definition = typed;
            return true;
        }

        return false;
    }

    public IReadOnlyList<RegistryEntry> GetByKind(ContentKind kind) =>
        _entries.TryGetValue(kind, out var byId)
            ? byId.Values.ToArray()
            : [];

    public bool TryGetMergedPluginConfig(string pluginId, string? characterId, out JsonObject? config)
    {
        config = null;
        var normalizedPluginId = NormalizeLookupKey(pluginId);
        if (string.IsNullOrEmpty(normalizedPluginId))
            return false;

        JsonObject? merged = null;
        if (TryGet<PluginDefaultConfigDefinition>(normalizedPluginId, out var defaults))
            merged = (JsonObject)defaults!.Config.DeepClone();

        var normalizedCharacterId = NormalizeLookupKey(characterId);
        if (!string.IsNullOrEmpty(normalizedCharacterId) &&
            TryGet<PluginCharacterConfigOverrideDefinition>($"{normalizedPluginId}:{normalizedCharacterId}", out var overrideConfig))
        {
            merged ??= [];
            MergeObjects(merged, overrideConfig!.Config);
        }

        if (merged is null)
            return false;

        config = merged;
        return true;
    }

    public IReadOnlyDictionary<string, JsonObject> GetMergedPluginConfigs(string? characterId, string? fallbackCharacterId = null)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var pluginIds = Entries
            .Select(entry => entry.Definition)
            .OfType<PluginDefaultConfigDefinition>()
            .Select(x => x.PluginId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var pluginId in pluginIds)
        {
            if (TryGetMergedPluginConfig(pluginId, characterId, out var direct))
            {
                result[pluginId] = direct!;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(fallbackCharacterId) &&
                TryGetMergedPluginConfig(pluginId, fallbackCharacterId, out var fallback))
            {
                result[pluginId] = fallback!;
                continue;
            }

            if (TryGetMergedPluginConfig(pluginId, null, out var defaults))
                result[pluginId] = defaults!;
        }

        return result;
    }

    private static ContentKind ResolveKind<TDefinition>() where TDefinition : ContentDefinitionBase
    {
        if (typeof(TDefinition) == typeof(CharacterArchetypeDefinition)) return ContentKind.CharacterArchetype;
        if (typeof(TDefinition) == typeof(ResourceDefinition)) return ContentKind.Resource;
        if (typeof(TDefinition) == typeof(ItemDefinition)) return ContentKind.Item;
        if (typeof(TDefinition) == typeof(ToolDefinition)) return ContentKind.Tool;
        if (typeof(TDefinition) == typeof(SkillDefinition)) return ContentKind.Skill;
        if (typeof(TDefinition) == typeof(PackManifest)) return ContentKind.PackManifest;
        if (typeof(TDefinition) == typeof(PluginDefaultConfigDefinition)) return ContentKind.PluginDefaultConfig;
        if (typeof(TDefinition) == typeof(PluginCharacterConfigOverrideDefinition)) return ContentKind.PluginCharacterConfigOverride;

        throw new NotSupportedException($"Unsupported content definition type: {typeof(TDefinition).FullName}");
    }

    private static string NormalizeLookupKey(string? value) => value?.Trim() ?? string.Empty;

    private static void MergeObjects(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is JsonObject sourceObject &&
                target[pair.Key] is JsonObject targetObject)
            {
                MergeObjects(targetObject, sourceObject);
                continue;
            }

            target[pair.Key] = pair.Value?.DeepClone();
        }
    }
}
