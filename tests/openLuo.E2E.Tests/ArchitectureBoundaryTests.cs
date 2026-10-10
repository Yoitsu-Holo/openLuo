using System.Reflection;
using openLuo.Client;
using openLuo.Protocol;
using openLuo.Server;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// 架构边界测试：把"靠人脑维持"的程序集边界变成会失败的断言。
/// 实现方式刻意不引入第三方库——直接读程序集元数据里的引用边
/// （等价于 csproj 的 ProjectReference），越界即失败。参见 docs/architecture/hub-protocol.md §3。
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    /// <summary>框架 / 运行时程序集（BCL、Extensions、MSBuild 生成物）。</summary>
    private static bool IsFramework(string name) =>
        name.StartsWith("System", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name.StartsWith("netstandard", StringComparison.Ordinal)
        || name.Equals("mscorlib", StringComparison.Ordinal)
        || name.StartsWith("xunit", StringComparison.Ordinal)
        || name.StartsWith("Newtonsoft", StringComparison.Ordinal);

    /// <summary>本仓程序集引用（有序，便于断言失败时直接读出实际值）。</summary>
    private static string[] RepoRefs(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !IsFramework(n))
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Protocol_零本仓依赖()
    {
        Assert.Empty(RepoRefs(typeof(Envelope).Assembly));
    }

    [Fact]
    public void Client_只依赖_Protocol()
    {
        Assert.Equal(["openLuo.Protocol"], RepoRefs(typeof(HubClient).Assembly));
    }

    [Fact]
    public void Server_不依赖宿主与内核其余部分()
    {
        var refs = RepoRefs(typeof(HubServer).Assembly);

        // 允许的边：Protocol（契约）+ Capabilities（第二公理，仅 wire 映射所需的类型）
        Assert.Contains("openLuo.Protocol", refs);
        Assert.Contains("openLuo.Capabilities", refs);

        // 禁止的边：宿主 exe 与内核其余部分
        foreach (var forbidden in new[]
                 {
                     "openLuo", "openLuo.Domain", "openLuo.AgentContext", "openLuo.Persistence",
                     "openLuo.Qqbot", "openLuo.Cli", "openLuo.Tui", "openLuo.Gui", "openLuo.OneBot",
                 })
        {
            Assert.DoesNotContain(forbidden, refs);
        }
    }

    [Fact]
    public void 扩展_不依赖宿主exe()
    {
        var extension = Assembly.Load("openLuo.Extension.Music");
        Assert.DoesNotContain("openLuo", RepoRefs(extension));
    }
}
