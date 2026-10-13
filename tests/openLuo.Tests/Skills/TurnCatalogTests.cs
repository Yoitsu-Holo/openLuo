using NSubstitute;
using openLuo.Abstractions;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core.Models;
using openLuo.Composition;
using Xunit;

namespace openLuo.Tests.Skills;

/// <summary>
/// 每轮目录的汇总（D30）：技能来自 <see cref="ISkillService"/>（扩展声明 + 文件加载都汇到它手里）、
/// 工作流来自扩展注册表、远程 Agent 来自已连接的 A2A 源。
/// </summary>
public sealed class TurnCatalogTests
{
    private static ExtensionRegistry RegistryWith(WorkflowDefinition[] workflows)
    {
        var registry = new ExtensionRegistry(Substitute.For<IServiceProvider>());
        registry.SetExtensions([new LoadedExtension { Id = "demo", Workflows = workflows }]);
        return registry;
    }

    [Fact]
    public void Skills_ComeFromSkillService()
    {
        var skills = new DefaultSkillService([
            new SkillDocument { Id = "demo:comfort", Title = "安慰", Summary = "s", WhenToUse = "用户难过时" },
        ]);
        var catalog = new RegisteredTurnCatalog(RegistryWith([]), skills, []);

        var summary = Assert.Single(catalog.Skills);
        Assert.Equal("demo:comfort", summary.Id);
        Assert.Equal("用户难过时", summary.WhenToUse);   // whenToUse 是模型判断"该不该用"的依据
    }

    [Fact]
    public void Workflows_ComeFromExtensionRegistry()
    {
        var catalog = new RegisteredTurnCatalog(
            RegistryWith([new WorkflowDefinition { Id = "world:gift", Description = "送礼流程" }]),
            new DefaultSkillService([]),
            []);

        var summary = Assert.Single(catalog.Workflows);
        Assert.Equal("world:gift", summary.Id);
        Assert.Equal("送礼流程", summary.Description);
    }

    [Fact]
    public void RemoteAgents_AreEmptyWithoutConnectedA2ASources()
    {
        // A2A 源在连接成功前没有 Agent Card → 目录里就不该出现（避免模型去调一个连不上的远端）
        var catalog = new RegisteredTurnCatalog(RegistryWith([]), new DefaultSkillService([]), []);

        Assert.Empty(catalog.RemoteAgents);
    }

    [Fact]
    public void NullCatalog_IsEmptySoUnwiredHostsBehaveAsBefore()
    {
        Assert.Empty(NullTurnCatalog.Instance.Skills);
        Assert.Empty(NullTurnCatalog.Instance.Workflows);
        Assert.Empty(NullTurnCatalog.Instance.RemoteAgents);
    }
}
