using openLuo.Modules.Content.Application;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Application.Validation;
using openLuo.Modules.Content.Core.Definitions;
using System.Text.Json.Nodes;

namespace openLuo.Content.Tests;

public sealed class ContentRegistryTests
{
    [Fact]
    public void Builder_BuildsRegistry_AndSupportsLookup()
    {
        var validator = new BasicContentValidator();
        var builder = new ContentRegistryBuilder(validator);

        var archetype = new CharacterArchetypeDefinition
        {
            Id = "bg.rin",
            DisplayName = "汐泠",
            CharacterName = "汐泠",
            Description = "测试角色",
            Prompt = "你是汐泠。"
        };
        var pack = new PackManifest
        {
            Id = "test.archetypes",
            DisplayName = "Test Archetypes",
            Contents =
            [
                new PackContentRef
                {
                    Kind = ContentKind.CharacterArchetype,
                    Id = archetype.Id
                }
            ]
        };

        builder.AddPack(pack);
        builder.AddDefinition(archetype, pack.Id);

        var registry = builder.Build();

        Assert.True(registry.TryGet<CharacterArchetypeDefinition>("bg.rin", out var resolved));
        Assert.NotNull(resolved);
        Assert.Equal("汐泠", resolved!.CharacterName);

        var entries = registry.GetByKind(ContentKind.CharacterArchetype);
        Assert.Single(entries);
        Assert.Equal(pack.Id, entries[0].SourcePack);
    }

    [Fact]
    public void Validator_FailsNegativePrice_AndMissingCharacterName()
    {
        var validator = new BasicContentValidator();
        var pack = new PackManifest
        {
            Id = "test.pack",
            DisplayName = "Test Pack"
        };

        var result = validator.Validate(pack,
        [
            new ItemDefinition
            {
                Id = "item.bad",
                DisplayName = "坏物品",
                Price = -1
            },
            new CharacterArchetypeDefinition
            {
                Id = "char.bad",
                DisplayName = "坏角色"
            }
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, x => x.Kind == ContentKind.Item && x.Message.Contains("negative", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, x => x.Kind == ContentKind.CharacterArchetype && x.Message.Contains("Character name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Registry_MergesPluginDefaultConfig_WithCharacterOverride()
    {
        var validator = new BasicContentValidator();
        var builder = new ContentRegistryBuilder(validator);

        var pack = new PackManifest
        {
            Id = "builtin.story",
            DisplayName = "Builtin Story"
        };

        builder.AddPack(pack);
        builder.AddDefinition(new PluginDefaultConfigDefinition
        {
            Id = "builtin_story",
            PluginId = "builtin_story",
            DisplayName = "builtin_story default config",
            Description = "default",
            Config = new JsonObject
            {
                ["limits"] = new JsonObject
                {
                    ["daily"] = 3,
                    ["allowedModes"] = new JsonArray("chat", "story")
                },
                ["enabled"] = true
            }
        }, pack.Id);
        builder.AddDefinition(new PluginCharacterConfigOverrideDefinition
        {
            Id = "builtin_story:builtin-rin",
            PluginId = "builtin_story",
            CharacterId = "builtin-rin",
            DisplayName = "builtin_story character override builtin-rin",
            Description = "override",
            Config = new JsonObject
            {
                ["limits"] = new JsonObject
                {
                    ["daily"] = 5
                },
                ["enabled"] = false,
                ["intro"] = "special"
            }
        }, pack.Id);

        var registry = builder.Build();

        Assert.True(registry.TryGetMergedPluginConfig("builtin_story", "builtin-rin", out var merged));
        Assert.NotNull(merged);
        Assert.Equal(5, merged!["limits"]!["daily"]!.GetValue<int>());
        Assert.Equal("chat", merged["limits"]!["allowedModes"]![0]!.GetValue<string>());
        Assert.False(merged["enabled"]!.GetValue<bool>());
        Assert.Equal("special", merged["intro"]!.GetValue<string>());

        Assert.True(registry.TryGetMergedPluginConfig("builtin_story", null, out var defaults));
        Assert.Equal(3, defaults!["limits"]!["daily"]!.GetValue<int>());
    }

    [Fact]
    public void ItemDefinitionCatalog_FindsByReference_AndId()
    {
        var validator = new BasicContentValidator();
        var builder = new ContentRegistryBuilder(validator);
        var pack = new PackManifest
        {
            Id = "test.items",
            DisplayName = "Test Items"
        };

        builder.AddPack(pack);
        builder.AddDefinition(new ItemDefinition
        {
            Id = "gift_lychee",
            DisplayName = "荔枝果冻",
            Price = 25,
            Tags = ["gift"]
        }, pack.Id);

        var registry = builder.Build();
        var catalog = new ItemDefinitionCatalog(registry);

        Assert.Equal("gift_lychee", catalog.GetById("GIFT_LYCHEE")?.Id);
        Assert.Equal("gift_lychee", catalog.FindByReference("荔枝果冻")?.Id);
        Assert.Equal("gift_lychee", catalog.FindByReference("送你一份荔枝果冻")?.Id);
    }
}
