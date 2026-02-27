using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Loaders;

public sealed class ItemContentPackFile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string LegacyName
    {
        get => DisplayName;
        set => DisplayName = value ?? string.Empty;
    }

    public string Description { get; set; } = string.Empty;

    public ItemDefinition[] Items { get; set; } = [];
}

public sealed class CanonicalItemContentPack
{
    public required PackManifest Manifest { get; init; }
    public required IReadOnlyList<ItemDefinition> Items { get; init; }
}

public static class ItemContentPackLoader
{
    public static IReadOnlyList<CanonicalItemContentPack> LoadAll(string baseDir)
    {
        var packs = new List<CanonicalItemContentPack>();
        var packsDir = Path.Combine(baseDir, "data", "item-packs");
        if (!Directory.Exists(packsDir)) return packs;

        foreach (var file in Directory.EnumerateFiles(packsDir, "*.jsonc", SearchOption.AllDirectories))
        {
            try
            {
                var pack = ParsePack(File.ReadAllText(file));
                if (pack is null) continue;

                var items = pack.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                    .ToArray();

                packs.Add(new CanonicalItemContentPack
                {
                    Manifest = new PackManifest
                    {
                        Id = string.IsNullOrWhiteSpace(pack.Id) ? "builtin.items" : pack.Id,
                        DisplayName = string.IsNullOrWhiteSpace(pack.DisplayName) ? pack.Id : pack.DisplayName,
                        Description = string.IsNullOrWhiteSpace(pack.Description)
                            ? "Item content pack loaded from data/item-packs."
                            : pack.Description,
                        Contents = items
                            .Select(item => new PackContentRef
                            {
                                Kind = ContentKind.Item,
                                Id = item.Id
                            })
                            .ToList()
                    },
                    Items = items
                });
            }
            catch
            {
                // Skip malformed item pack files without breaking the registry build.
            }
        }

        return packs;
    }

    private static ItemContentPackFile? ParsePack(string json)
    {
        var root = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject;
        if (root is null) return null;

        return new ItemContentPackFile
        {
            Id = root["id"]?.GetValue<string>() ?? string.Empty,
            DisplayName = root["displayName"]?.GetValue<string>()
                ?? root["name"]?.GetValue<string>()
                ?? string.Empty,
            Description = root["description"]?.GetValue<string>() ?? string.Empty,
            Items = (root["items"] as JsonArray)?
                .Select(ParseItem)
                .Where(item => item is not null)
                .Cast<ItemDefinition>()
                .ToArray()
                ?? []
        };
    }

    private static ItemDefinition? ParseItem(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;

        return new ItemDefinition
        {
            Id = obj["id"]?.GetValue<string>() ?? string.Empty,
            DisplayName = obj["displayName"]?.GetValue<string>()
                ?? obj["name"]?.GetValue<string>()
                ?? string.Empty,
            Description = obj["description"]?.GetValue<string>() ?? string.Empty,
            Price = obj["price"]?.GetValue<int?>() ?? 0,
            Rarity = obj["rarity"]?.GetValue<string>() ?? "Common",
            Tags = (obj["tags"] as JsonArray)?
                .Select(tag => tag?.GetValue<string>() ?? string.Empty)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToArray()
                ?? [],
            AffectionDelta = obj["affectionDelta"]?.GetValue<int?>() ?? 0,
            MoodEffect = obj["moodEffect"]?.GetValue<string>()
        };
    }
}
