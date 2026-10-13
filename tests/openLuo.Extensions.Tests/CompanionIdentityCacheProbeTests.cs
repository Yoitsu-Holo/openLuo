using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using OpenLuo.Extensions.Companion;
using Xunit;

namespace openLuo.Extensions.Tests;

/// <summary>
/// 人格块的**版本探针**（D46）：探针只 stat 角色档案、不解析，用来让"编辑档案 → 下一回合立刻生效"
/// 成立。这里锁住两件事：探针随文件变化而变化；探针自身足够廉价（不依赖解析结果）。
/// </summary>
public sealed class CompanionIdentityCacheProbeTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"openluo-archetypes-{Guid.NewGuid():N}");

    public CompanionIdentityCacheProbeTests() => Directory.CreateDirectory(Path.Combine(_dataDir, "archetypes"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }

    private string WriteArchetype(string prompt)
    {
        var path = Path.Combine(_dataDir, "archetypes", "builtin-rin.jsonc");
        File.WriteAllText(path, $$"""
            {
              "id": "builtin-rin",
              "displayName": "洛天依",
              "description": "测试角色",
              "prompt": "{{prompt}}"
            }
            """);
        return path;
    }

    private static ContextBuildRequest Request() => new() { SessionId = "s", SubjectId = "builtin-rin", TurnId = "t" };

    [Fact]
    public void Probe_IsStableWhileFileUnchanged()
    {
        WriteArchetype("v1");
        var contributor = new CompanionIdentityContributor(_dataDir);

        var a = contributor.CurrentVersion(Request());
        var b = contributor.CurrentVersion(Request());

        Assert.NotNull(a);
        Assert.Equal(a, b);          // 文件没动 → 版本不变 → 缓存可复用
    }

    [Fact]
    public void Probe_ChangesWhenArchetypeIsEdited()
    {
        var path = WriteArchetype("v1");
        var contributor = new CompanionIdentityContributor(_dataDir);
        var before = contributor.CurrentVersion(Request());

        // 显式推进 mtime：测试不该依赖文件系统的写入时间戳精度
        WriteArchetype("v2");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var after = contributor.CurrentVersion(Request());

        Assert.NotEqual(before, after);   // 编辑档案 → 版本变化 → 下一回合立刻重建
    }

    [Fact]
    public void Probe_ReturnsNullWhenArchetypeDirectoryMissing()
    {
        var contributor = new CompanionIdentityContributor(Path.Combine(_dataDir, "does-not-exist"));

        Assert.Null(contributor.CurrentVersion(Request()));   // 拿不到版本 → 退回 TTL 兜底
    }
}
