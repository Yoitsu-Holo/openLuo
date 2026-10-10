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

    /// <summary>内核 + Hub：客户端进程**一律不许**引用其中任何一个（客户端只经协议连 Hub）。</summary>
    private static readonly string[] KernelAssemblies =
    [
        "openLuo", "openLuo.Server", "openLuo.Abstractions",
        "openLuo.Capabilities", "openLuo.Capabilities.Mcp", "openLuo.Capabilities.Llm", "openLuo.Capabilities.A2A",
        "openLuo.AgentContext", "openLuo.Domain", "openLuo.Memory", "openLuo.Llm", "openLuo.Embedding",
        "openLuo.Foundation", "openLuo.Modules.Agent", "openLuo.Modules.WorldState", "openLuo.Persistence",
    ];

    /// <summary>UI / 协议驱动：每个客户端只允许出现"自己那份"。</summary>
    private static bool IsDriver(string name) =>
        name.StartsWith("Avalonia", StringComparison.Ordinal)
        || name.StartsWith("CommunityToolkit", StringComparison.Ordinal)
        || name.StartsWith("SkiaSharp", StringComparison.Ordinal)
        || name.StartsWith("Terminal.Gui", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
        || name.StartsWith("Milky", StringComparison.Ordinal)
        || name.StartsWith("openLuo.OneBot", StringComparison.Ordinal);

    [Fact]
    public void 服务端不含客户端与UI驱动()
    {
        var refs = RepoRefs(Assembly.Load("openLuo"));
        foreach (var client in new[] { "openluo-cli", "openluo-tui", "openluo-gui", "openluo-qq", "openLuo.OneBot", "openLuo.Client" })
            Assert.DoesNotContain(client, refs);
        Assert.Empty(refs.Where(IsDriver));
    }

    [Fact]
    public void 客户端不带内核且不互相暴露()
    {
        // 每个客户端：只允许 Protocol + Client + 自己那份驱动
        var owners = new (string Assembly, string[] OwnDrivers)[]
        {
            ("openluo-cli", []),
            ("openluo-tui", ["Terminal.Gui"]),
            ("openluo-gui", ["Avalonia", "CommunityToolkit", "SkiaSharp"]),
            ("openluo-qq", ["openLuo.OneBot"]),
        };

        foreach (var (name, ownDrivers) in owners)
        {
            var refs = RepoRefs(Assembly.Load(name));

            Assert.Empty(refs.Intersect(KernelAssemblies));
            Assert.Contains("openLuo.Protocol", refs);
            Assert.Contains("openLuo.Client", refs);

            var foreignDrivers = refs
                .Where(IsDriver)
                .Where(r => !ownDrivers.Any(prefix => r.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            Assert.Empty(foreignDrivers);
        }
    }
}
