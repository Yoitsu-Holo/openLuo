using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using openLuo.Capabilities.A2A;
using openLuo.Capabilities.Mcp;
using openLuo.Modules.Embedding.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Qqbot;
using Xunit;

namespace openLuo.Tests.Config;

/// <summary>
/// 配置面守卫：`openLuo/data/config/*.example.jsonc` 必须与 C# 模型**双向一致**。
/// <para>
/// 背景：这里出过真实的"配置骗人"事故——示例里写着已删除的键（`security` 段 9 个旧键）、
/// 代码读的段落示例完全没写（`log.hot` / `log.archive`）、有读取实现却没有随包模板（`a2a-agents.jsonc`）。
/// 光靠人肉比对必然再漂，所以把不变量钉进测试：
/// </para>
/// <list type="number">
/// <item>示例里**不允许**出现模型没有的键（过时键 / 凭空键）；</item>
/// <item>模型里每个字段都必须在示例里**说明过**（真实键，或以注释形式给出——密钥类字段有意不落盘）；</item>
/// <item>`log.categories` 的名字必须来自「代码实际会打日志的分类」白名单。</item>
/// </list>
/// </summary>
public sealed class ConfigExampleSurfaceTests
{
    /// <summary>代码里实际会打日志的 category（改代码新增分类时同步这里；示例不得出现白名单外的名字）。</summary>
    private static readonly HashSet<string> LiveLogCategories =
        new(StringComparer.Ordinal) { "llm", "embedding", "memory", "plugin", "agent/dispatch", "agent/context" };

    private static string ConfigDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "openLuo.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);   // 找不到仓库根 → 测试环境异常，而不是"通过"
        var configDir = Path.Combine(dir!.FullName, "openLuo", "data", "config");
        Assert.True(Directory.Exists(configDir), $"示例配置目录不存在: {configDir}");
        return configDir;
    }

    private static JsonDocument LoadExample(string fileName)
    {
        var path = Path.Combine(ConfigDir(), fileName);
        Assert.True(File.Exists(path), $"示例文件缺失: {fileName}");
        return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }

    /// <summary>JSON 侧键名 → C# 属性（camelCase；显式 [JsonPropertyName] 以注解为准）。</summary>
    private static string JsonName(PropertyInfo p) =>
        p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? char.ToLowerInvariant(p.Name[0]) + p.Name[1..];

    private static HashSet<string> JsonNames(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(JsonName)];

    /// <summary>逐文件：文件 → (JSON 内的段路径, 对应模型类型)。段路径为空表示根即模型（qqbot）。</summary>
    private static readonly (string File, string[] Path, Type Model)[] SectionFiles =
    [
        ("agent.example.jsonc", ["agent"], typeof(AgentRuntimeConfig)),
        ("embedding.example.jsonc", ["embedding"], typeof(EmbeddingConfig)),
        ("log.example.jsonc", ["log"], typeof(LogConfig)),
        ("security.example.jsonc", ["security"], typeof(SecurityRuntimeConfig)),
        ("memory.example.jsonc", ["sqliteVec"], typeof(openLuo.Modules.Memory.Core.Models.SqliteVecConfig)),
        ("qqbot.example.jsonc", [], typeof(QqBotConfig)),
    ];

    /// <summary>数组/嵌套：文件 → (JSON 路径, 模型类型)。</summary>
    private static readonly (string File, string[] Path, Type Model)[] NestedFiles =
    [
        ("llm.example.jsonc", ["llm", "routes"], typeof(LlmRouteConfig)),
        ("llm.example.jsonc", ["llm", "routes", "capabilities"], typeof(LlmCapabilitiesConfig)),
        ("llm.example.jsonc", ["llm", "routing"], typeof(LlmRoutingConfig)),
        ("log.example.jsonc", ["log", "hot"], typeof(LogHotConfig)),
        ("log.example.jsonc", ["log", "archive"], typeof(LogArchiveConfig)),
        ("mcp-servers.example.jsonc", ["servers"], typeof(McpServerConfig)),
        ("a2a-agents.example.jsonc", ["agents"], typeof(A2AAgentConfig)),
    ];

    private static IEnumerable<JsonElement> ObjectsAt(JsonDocument doc, string[] path)
    {
        var node = doc.RootElement;
        foreach (var part in path)
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out var next))
                yield break;
            node = next;
        }
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object)
                    yield return item;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
        }
    }

    [Fact]
    public void Examples_DoNotContainKeysUnknownToModels()
    {
        var problems = new List<string>();

        foreach (var (file, path, model) in SectionFiles.Concat(NestedFiles))
        {
            using var doc = LoadExample(file);
            var allowed = JsonNames(model);
            foreach (var obj in ObjectsAt(doc, path))
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    if (!allowed.Contains(prop.Name))
                        problems.Add($"{file} {string.Join('.', path)}: 键 `{prop.Name}` 在 {model.Name} 上不存在");
                }
            }
        }

        // databasePath 是根级标量键（没有对应"段"）
        using (var doc = LoadExample("database.example.jsonc"))
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name != "databasePath")
                    problems.Add($"database.example.jsonc: 未知根键 `{prop.Name}`");
            }
        }

        // log.categories 的名字必须来自实产分类
        using (var doc = LoadExample("log.example.jsonc"))
        {
            if (doc.RootElement.GetProperty("log").TryGetProperty("categories", out var cats))
            {
                foreach (var c in cats.EnumerateObject())
                {
                    if (!LiveLogCategories.Contains(c.Name))
                        problems.Add($"log.example.jsonc categories: `{c.Name}` 没有任何打日志点");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Examples_DocumentEveryModelProperty()
    {
        var problems = new List<string>();

        foreach (var (file, path, model) in SectionFiles.Concat(NestedFiles))
        {
            using var doc = LoadExample(file);
            var raw = File.ReadAllText(Path.Combine(ConfigDir(), file));
            var documented = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obj in ObjectsAt(doc, path))
            {
                foreach (var prop in obj.EnumerateObject())
                    documented.Add(prop.Name);
            }

            foreach (var name in JsonNames(model))
            {
                if (documented.Contains(name))
                    continue;
                // 允许"以注释形式说明"（密钥类字段有意不落盘，如 mcp 的 env）
                if (raw.Contains($"\"{name}\"", StringComparison.Ordinal))
                    continue;
                problems.Add($"{file} {string.Join('.', path)}: 模型属性 `{name}`（{model.Name}）未在示例中说明");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void DatabaseExample_DocumentsDatabasePath()
    {
        using var doc = LoadExample("database.example.jsonc");
        Assert.True(doc.RootElement.TryGetProperty("databasePath", out _));
        // AppConfig 上的根级标量必须与示例同名（JsonNames 已转为 JSON 侧 camelCase）
        Assert.Contains("databasePath", JsonNames(typeof(AppConfig)));
    }
}
