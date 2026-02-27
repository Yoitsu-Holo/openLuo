using System.Text.Json;
using System.Text.RegularExpressions;

namespace openLuo.Integration.Tests;

public class BuiltinPluginNamingTests
{
    private static readonly string[] ExpectedPluginDirectories =
    [
        "builtin_asset_bg_generator",
        "builtin_asset_cg_generator",
        "builtin_asset_gallery",
        "builtin_char_status_daily",
        "builtin_char_status_intimate",
        "builtin_char_status_relationship",
        "builtin_event_date",
        "builtin_event_diary",
        "builtin_game_resource_core",
        "builtin_inventory_shop",
        "builtin_skill_core",
        "builtin_subagent_core",
        "builtin_system_commands",
        "builtin_system_lifecycle",
        "builtin_system_status",
        "builtin_system_work",
        "builtin_world_state_core",
        "example_dream_weaver"
    ];

    private static readonly string[] StatePluginDirectories =
    [
        "builtin_char_status_daily",
        "builtin_char_status_intimate",
        "builtin_char_status_relationship",
        "builtin_game_resource_core"
    ];

    [Fact]
    public void BuiltinPluginDirectories_AndManifestIds_ShouldUseUnifiedNamingConvention()
    {
        var pluginRoot = GetPluginRoot();
        var actualDirectories = Directory.EnumerateDirectories(pluginRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("__", StringComparison.Ordinal) && !string.Equals(name, "shared", StringComparison.Ordinal))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedPluginDirectories, actualDirectories);

        foreach (var directory in actualDirectories)
        {
            var manifestPath = Path.Combine(pluginRoot, directory, "plugin.jsonc");
            Assert.True(File.Exists(manifestPath), $"{directory} 缺少 plugin.jsonc");

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.Equal(directory, doc.RootElement.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void BuiltinPluginPythonMetadata_ShouldMatchManifestId_WhenDeclared()
    {
        var pluginRoot = GetPluginRoot();

        foreach (var directory in ExpectedPluginDirectories)
        {
            var manifestPath = Path.Combine(pluginRoot, directory, "plugin.jsonc");
            var mainPath = Path.Combine(pluginRoot, directory, "main.py");
            Assert.True(File.Exists(mainPath), $"{directory} 缺少 main.py");

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var expectedId = doc.RootElement.GetProperty("id").GetString();
            var source = File.ReadAllText(mainPath);

            foreach (Match match in Regex.Matches(source, "PLUGIN_ID\\s*=\\s*\"([^\"]+)\""))
            {
                Assert.Equal(expectedId, match.Groups[1].Value);
            }

            foreach (Match match in Regex.Matches(source, "\\\"serverInfo\\\"\\s*:\\s*\\{\\s*\\\"name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.Singleline))
            {
                Assert.Equal(expectedId, match.Groups[1].Value);
            }
        }
    }

    [Fact]
    public void StatePlugins_ShouldUseStateSemanticManifestAndDefinitionFile()
    {
        var pluginRoot = GetPluginRoot();

        foreach (var directory in StatePluginDirectories)
        {
            var manifestPath = Path.Combine(pluginRoot, directory, "plugin.jsonc");
            var stateDefsPath = Path.Combine(pluginRoot, directory, "state_defs.jsonc");
            var legacyResourcesPath = Path.Combine(pluginRoot, directory, "resources.jsonc");

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.Equal("state", doc.RootElement.GetProperty("pluginType").GetString());

            Assert.True(File.Exists(stateDefsPath), $"{directory} 缺少 state_defs.jsonc");
            Assert.False(File.Exists(legacyResourcesPath), $"{directory} 不应保留 resources.jsonc");
        }
    }

    private static string GetPluginRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "openLuo.sln")))
            {
                return Path.Combine(dir.FullName, "openLuo", "data", "plugins");
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录或插件目录。");
    }
}
