using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Loaders;

public static class PluginContentLoader
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<PackManifest> LoadRuntimePluginManifests(string baseDir)
    {
        var pluginsDir = Path.Combine(baseDir, "data", "plugins");
        if (!Directory.Exists(pluginsDir))
            return [];

        var manifests = new List<PackManifest>();
        foreach (var dir in Directory.EnumerateDirectories(pluginsDir))
        {
            var manifestPath = Path.Combine(dir, "plugin.jsonc");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var raw = JsonSerializer.Deserialize<PluginManifestDoc>(File.ReadAllText(manifestPath), _opts);
                if (raw is null || string.IsNullOrWhiteSpace(raw.Id))
                    continue;
                if (raw.Disabled)
                    continue;

                manifests.Add(new PackManifest
                {
                    Id = raw.Id,
                    DisplayName = string.IsNullOrWhiteSpace(raw.Name) ? raw.Id : raw.Name,
                    Description = raw.Description ?? "Runtime plugin manifest compatibility view.",
                    ContentVersion = raw.Version ?? "1.0.0",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["pack_kind"] = "runtime-plugin",
                        ["entry"] = raw.Entry ?? string.Empty,
                        ["source_kind"] = "plugin_manifest"
                    }
                });
            }
            catch
            {
                // 忽略损坏 manifest，保持与当前宽松内容加载风格一致
            }
        }

        return manifests;
    }

    public static IReadOnlyList<ResourceDefinition> LoadResourceDefinitions(string baseDir)
    {
        var pluginsDir = Path.Combine(baseDir, "data", "plugins");
        if (!Directory.Exists(pluginsDir))
            return [];

        var definitions = new List<ResourceDefinition>();
        foreach (var pluginDir in EnumeratePluginDirectories(baseDir))
        {
            if (IsPluginDisabled(pluginDir))
                continue;

            var file = Path.Combine(pluginDir, "state_defs.jsonc");
            if (!File.Exists(file))
                continue;

            try
            {
                var raws = JsonSerializer.Deserialize<List<ResourceDefinitionDoc>>(File.ReadAllText(file), _opts);
                if (raws is null)
                    continue;

                foreach (var raw in raws)
                {
                    if (string.IsNullOrWhiteSpace(raw.Namespace) || string.IsNullOrWhiteSpace(raw.Key))
                        continue;

                    definitions.Add(new ResourceDefinition
                    {
                        Id = $"{raw.Namespace}.{raw.Key}",
                        DisplayName = raw.Metadata?.Name ?? raw.Key,
                        Description = raw.PromptContext ?? string.Empty,
                        ResourceType = raw.ValueType ?? string.Empty,
                        Unit = raw.DisplayFormat ?? string.Empty,
                        InitialValue = ParseDecimal(raw.DefaultValue),
                        MinValue = ParseDecimal(raw.Min),
                        MaxValue = ParseDecimal(raw.Max),
                        Tags = raw.Metadata?.Category is { Length: > 0 } category ? [category] : [],
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["namespace"] = raw.Namespace,
                            ["key"] = raw.Key,
                            ["owner_kind"] = raw.OwnerKind ?? string.Empty,
                            ["mutable_by_llm"] = raw.MutableByLlm.ToString(),
                            ["derived"] = raw.Derived.ToString(),
                            ["source_kind"] = "state_defs"
                        }
                    });
                }
            }
            catch
            {
                // 忽略损坏资源定义，保持当前宽松加载策略
            }
        }

        return definitions;
    }

    public static IReadOnlyList<PluginDefaultConfigDefinition> LoadDefaultConfigs(string baseDir)
    {
        var definitions = new List<PluginDefaultConfigDefinition>();
        foreach (var pluginDir in EnumeratePluginDirectories(baseDir))
        {
            if (IsPluginDisabled(pluginDir))
                continue;

            var pluginId = Path.GetFileName(pluginDir);
            var configPath = ResolveDefaultConfigPath(pluginDir);
            if (configPath is null)
                continue;

            var config = TryLoadJsonObject(configPath);
            if (config is null)
                continue;

            definitions.Add(new PluginDefaultConfigDefinition
            {
                Id = pluginId,
                PluginId = pluginId,
                DisplayName = $"{pluginId} default config",
                Description = "Plugin default config.",
                Config = config,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["plugin_id"] = pluginId,
                    ["source_kind"] = "plugin_default_config",
                    ["source_path"] = configPath
                }
            });
        }

        return definitions;
    }

    public static IReadOnlyList<PluginCharacterConfigOverrideDefinition> LoadCharacterConfigOverrides(string baseDir)
    {
        var definitions = new List<PluginCharacterConfigOverrideDefinition>();
        foreach (var pluginDir in EnumeratePluginDirectories(baseDir))
        {
            if (IsPluginDisabled(pluginDir))
                continue;

            var pluginId = Path.GetFileName(pluginDir);
            foreach (var (characterId, path) in EnumerateCharacterOverridePaths(pluginDir))
            {
                var config = TryLoadJsonObject(path);
                if (config is null)
                    continue;

                definitions.Add(new PluginCharacterConfigOverrideDefinition
                {
                    Id = BuildCharacterOverrideId(pluginId, characterId),
                    PluginId = pluginId,
                    CharacterId = characterId,
                    DisplayName = $"{pluginId} character override {characterId}",
                    Description = "Plugin character override config.",
                    Config = config,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["plugin_id"] = pluginId,
                        ["character_id"] = characterId,
                        ["source_kind"] = "plugin_character_config_override",
                        ["source_path"] = path
                    }
                });
            }
        }

        return definitions;
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static IEnumerable<string> EnumeratePluginDirectories(string baseDir)
    {
        var pluginsDir = Path.Combine(baseDir, "data", "plugins");
        if (!Directory.Exists(pluginsDir))
            return [];

        return Directory.EnumerateDirectories(pluginsDir);
    }

    private static string? ResolveDefaultConfigPath(string pluginDir)
    {
        var candidates = new[]
        {
            Path.Combine(pluginDir, "defaults.jsonc"),
            Path.Combine(pluginDir, "defaults.json"),
            Path.Combine(pluginDir, "config", "default.jsonc"),
            Path.Combine(pluginDir, "config", "default.json"),
            Path.Combine(pluginDir, "config.jsonc"),
            Path.Combine(pluginDir, "config.json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<(string CharacterId, string Path)> EnumerateCharacterOverridePaths(string pluginDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var characterDir = Path.Combine(pluginDir, "characters");
        if (Directory.Exists(characterDir))
        {
            foreach (var path in Directory.EnumerateFiles(characterDir, "*.json*", SearchOption.TopDirectoryOnly))
            {
                var characterId = Path.GetFileNameWithoutExtension(path);
                if (seen.Add(characterId))
                    yield return (characterId, path);
            }
        }

        var legacyConfigDir = Path.Combine(pluginDir, "config");
        if (Directory.Exists(legacyConfigDir))
        {
            foreach (var path in Directory.EnumerateFiles(legacyConfigDir, "*.json*", SearchOption.TopDirectoryOnly))
            {
                var characterId = Path.GetFileNameWithoutExtension(path);
                if (characterId.Equals("default", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (seen.Add(characterId))
                    yield return (characterId, path);
            }
        }
    }

    private static JsonObject? TryLoadJsonObject(string path)
    {
        try
        {
            var node = JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            return node as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildCharacterOverrideId(string pluginId, string characterId) =>
        $"{pluginId}:{characterId}";

    private static bool IsPluginDisabled(string pluginDir)
    {
        var manifestPath = Path.Combine(pluginDir, "plugin.jsonc");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            var raw = JsonSerializer.Deserialize<PluginManifestDoc>(File.ReadAllText(manifestPath), _opts);
            return raw?.Disabled == true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PluginManifestDoc
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Version { get; init; }
        public string? Entry { get; init; }
        public string? Description { get; init; }
        public bool Disabled { get; init; }
    }

    private sealed class ResourceDefinitionDoc
    {
        public string? Namespace { get; init; }
        public string? Key { get; init; }
        public string? OwnerKind { get; init; }
        public string? ValueType { get; init; }
        public string? DefaultValue { get; init; }
        public bool MutableByLlm { get; init; }
        public bool Derived { get; init; }
        public string? DisplayFormat { get; init; }
        public string? PromptContext { get; init; }
        public string? Min { get; init; }
        public string? Max { get; init; }
        public ResourceMetadataDoc? Metadata { get; init; }
    }

    private sealed class ResourceMetadataDoc
    {
        public string? Name { get; init; }
        public string? Category { get; init; }
    }
}
