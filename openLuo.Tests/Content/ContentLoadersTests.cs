using openLuo.Modules.Content.Application.Loaders;

namespace openLuo.Content.Tests;

public sealed class ContentLoadersTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), $"openluo-content-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CharacterArchetypeLoader_LoadAll_ParsesCanonicalArchetypeFile()
    {
        Directory.CreateDirectory(Path.Combine(_baseDir, "data", "archetypes"));
        File.WriteAllText(
            Path.Combine(_baseDir, "data", "archetypes", "test.jsonc"),
            """
            {
              "id": "test-cat",
              "displayName": "测试猫娘",
              "characterName": "铃",
              "description": "测试角色",
              "prompt": "你是铃。",
              "backstory": "来自测试。",
              "initialLocation": "测试房间",
              "socialStyle": "outgoing",
              "outingInitiativeFrom": "friend",
              "moodAffectsInitiative": true,
              "traits": ["好奇"]
            }
            """);

        var result = CharacterArchetypeLoader.LoadAll(_baseDir);

        var archetype = Assert.Single(result);
        Assert.Equal("test-cat", archetype.Id);
        Assert.Equal("测试猫娘", archetype.DisplayName);
        Assert.Equal("铃", archetype.CharacterName);
        Assert.Equal("你是铃。", archetype.Prompt);
    }

    [Fact]
    public void ItemContentPackLoader_LoadAll_ParsesItemPack()
    {
        Directory.CreateDirectory(Path.Combine(_baseDir, "data", "item-packs"));
        File.WriteAllText(
            Path.Combine(_baseDir, "data", "item-packs", "items.jsonc"),
            """
            {
              "id": "builtin-items",
              "displayName": "内置物品",
              "items": [
                {
                  "id": "flower",
                  "displayName": "花束",
                  "description": "节日礼物",
                  "price": 30,
                  "rarity": "Rare"
                }
              ]
            }
            """);

        var packs = ItemContentPackLoader.LoadAll(_baseDir);

        var pack = Assert.Single(packs);
        Assert.Equal("builtin-items", pack.Manifest.Id);
        var item = Assert.Single(pack.Items);
        Assert.Equal("flower", item.Id);
        Assert.Equal("花束", item.DisplayName);
        Assert.Equal("Rare", item.Rarity);
    }

    [Fact]
    public void ToolAndSkillLoaders_LoadMarkdownAssets()
    {
        Directory.CreateDirectory(Path.Combine(_baseDir, "data", "tools"));
        Directory.CreateDirectory(Path.Combine(_baseDir, "data", "skills"));
        File.WriteAllText(Path.Combine(_baseDir, "data", "tools", "ask.md"), "# 询问工具\n这是一个测试工具。");
        File.WriteAllText(Path.Combine(_baseDir, "data", "skills", "guard.md"), "# 守护技能\n这是一个测试技能。");

        var tools = ToolDocLoader.LoadAll(_baseDir);
        var skills = SkillDocLoader.LoadAll(_baseDir);

        var tool = Assert.Single(tools);
        Assert.Equal("ask", tool.Id);
        Assert.Equal("询问工具", tool.DisplayName);
        Assert.Equal("这是一个测试工具。", tool.Description);

        var skill = Assert.Single(skills);
        Assert.Equal("guard", skill.Id);
        Assert.Equal("守护技能", skill.DisplayName);
        Assert.Contains("测试技能", skill.Body);
    }

    [Fact]
    public void PluginContentLoader_LoadsPluginManifest_AndStateDefinitions()
    {
        var pluginDir = Path.Combine(_baseDir, "data", "plugins", "builtin_demo");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(
            Path.Combine(pluginDir, "plugin.jsonc"),
            """
            {
              "id": "builtin_demo",
              "name": "Builtin Demo",
              "version": "1.2.0",
              "entry": "main.py",
              "description": "测试插件"
            }
            """);
        File.WriteAllText(
            Path.Combine(pluginDir, "state_defs.jsonc"),
            """
            [
              {
                "namespace": "char_status",
                "key": "trust",
                "ownerKind": "character",
                "valueType": "number",
                "defaultValue": "50",
                "mutableByLlm": true,
                "derived": false,
                "displayFormat": "信任：{value}/100",
                "promptContext": "信任影响回答。",
                "min": "0",
                "max": "100",
                "metadata": {
                  "name": "信任度",
                  "category": "intimacy"
                }
              }
            ]
            """);

        var manifests = PluginContentLoader.LoadRuntimePluginManifests(_baseDir);
        var resources = PluginContentLoader.LoadResourceDefinitions(_baseDir);

        var manifest = Assert.Single(manifests);
        Assert.Equal("builtin_demo", manifest.Id);
        Assert.Equal("Builtin Demo", manifest.DisplayName);
        Assert.Equal("runtime-plugin", manifest.Metadata["pack_kind"]);

        var resource = Assert.Single(resources);
        Assert.Equal("char_status.trust", resource.Id);
        Assert.Equal("信任度", resource.DisplayName);
        Assert.Equal("number", resource.ResourceType);
        Assert.Equal(50m, resource.InitialValue);
    }

    [Fact]
    public void PluginContentLoader_LoadsDefaultConfigs_AndCharacterOverrides_FromCanonicalAndLegacyPaths()
    {
        var canonicalPluginDir = Path.Combine(_baseDir, "data", "plugins", "builtin_story");
        Directory.CreateDirectory(Path.Combine(canonicalPluginDir, "characters"));
        File.WriteAllText(
            Path.Combine(canonicalPluginDir, "defaults.jsonc"),
            """
            {
              // default config
              "limits": {
                "daily": 3,
                "allowedModes": ["chat", "story"]
              },
              "enabled": true
            }
            """);
        File.WriteAllText(
            Path.Combine(canonicalPluginDir, "characters", "builtin-rin.jsonc"),
            """
            {
              "limits": {
                "daily": 5
              },
              "intro": "special"
            }
            """);

        var legacyPluginDir = Path.Combine(_baseDir, "data", "plugins", "builtin_legacy");
        Directory.CreateDirectory(Path.Combine(legacyPluginDir, "config"));
        File.WriteAllText(
            Path.Combine(legacyPluginDir, "config.jsonc"),
            """
            {
              "maxChatLoops": 6
            }
            """);
        File.WriteAllText(
            Path.Combine(legacyPluginDir, "config", "builtin-ling.jsonc"),
            """
            {
              "maxChatLoops": 8
            }
            """);

        var defaults = PluginContentLoader.LoadDefaultConfigs(_baseDir);
        var overrides = PluginContentLoader.LoadCharacterConfigOverrides(_baseDir);

        Assert.Equal(2, defaults.Count);
        var canonicalDefault = Assert.Single(defaults, x => x.PluginId == "builtin_story");
        Assert.Equal(3, canonicalDefault.Config["limits"]!["daily"]!.GetValue<int>());
        Assert.True(canonicalDefault.Config["enabled"]!.GetValue<bool>());

        var legacyDefault = Assert.Single(defaults, x => x.PluginId == "builtin_legacy");
        Assert.Equal(6, legacyDefault.Config["maxChatLoops"]!.GetValue<int>());

        Assert.Equal(2, overrides.Count);
        var canonicalOverride = Assert.Single(overrides, x => x.PluginId == "builtin_story");
        Assert.Equal("builtin-rin", canonicalOverride.CharacterId);
        Assert.Equal("special", canonicalOverride.Config["intro"]!.GetValue<string>());

        var legacyOverride = Assert.Single(overrides, x => x.PluginId == "builtin_legacy");
        Assert.Equal("builtin-ling", legacyOverride.CharacterId);
        Assert.Equal(8, legacyOverride.Config["maxChatLoops"]!.GetValue<int>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, recursive: true);
    }
}
