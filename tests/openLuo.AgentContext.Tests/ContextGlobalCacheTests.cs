using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using Xunit;

namespace openLuo.AgentContext.Tests;

/// <summary>
/// 全局块缓存（D46，解 R2/R5）：同一角色的**所有会话引用同一份**全局块，不再各自重算/复制。
/// 这里锁住的是缓存的三条边界——只缓存 Global + 显式 TTL 的贡献者、必须能显式失效、TTL 到期要重算。
/// </summary>
public sealed class ContextGlobalCacheTests
{
    private sealed class FakeContributor : IContextContributor
    {
        public string Id { get; init; } = "fake";
        public ContextScope Scope { get; init; } = ContextScope.Global;
        public TimeSpan CacheTtl { get; init; } = TimeSpan.Zero;

        /// <summary>版本探针：null = 没有探针（走 TTL）；抛异常 = 探针失败（也应退回 TTL）。</summary>
        public Func<string?>? VersionProbe { get; init; }

        public int Probes { get; private set; }
        public int Calls { get; private set; }

        public string? CurrentVersion(ContextBuildRequest request)
        {
            Probes++;
            return VersionProbe?.Invoke();
        }

        public Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
                Contributions = [new ContextContribution
                {
                    Id = $"{Id}:{request.SubjectId}:{Calls}", ContributorId = Id, Content = $"call-{Calls}",
                }],
            });
        }
    }

    private static ContextBuildRequest Request(string subjectId = "builtin-rin") => new()
    {
        SessionId = "sess_1", SubjectId = subjectId, TurnId = "t1",
    };

    [Fact]
    public async Task GlobalContributorWithTtl_IsCalledOnceWithinTtl()
    {
        var contributor = new FakeContributor { CacheTtl = TimeSpan.FromMinutes(1) };
        var assembler = new DefaultContextAssembler([contributor]);

        var first = await assembler.BuildAsync(Request());
        var second = await assembler.BuildAsync(Request());

        Assert.Equal(1, contributor.Calls);                 // 第二次直接复用，没再调用
        Assert.Equal(new ContextCacheStats(0, 1), first.Cache);
        Assert.Equal(new ContextCacheStats(1, 0), second.Cache);
        Assert.Contains("call-1", string.Join("", second.Contributions.Select(c => c.Content)));
    }

    [Fact]
    public async Task CachedContributions_AreSharedByReference()
    {
        // "引用而非复制"（R2）：同一角色的所有会话拿到的是同一批不可变贡献实例
        var contributor = new FakeContributor { CacheTtl = TimeSpan.FromMinutes(1) };
        var assembler = new DefaultContextAssembler([contributor]);

        var first = await assembler.BuildAsync(Request());
        var second = await assembler.BuildAsync(Request());

        Assert.Same(first.Contributions[0], second.Contributions[0]);
    }

    [Fact]
    public async Task NonGlobalScope_IgnoresTtl()
    {
        // 会话域的内容必须每回合新鲜：即使写了 TTL 也不缓存（否则会跨回合冻结）
        var contributor = new FakeContributor { Scope = ContextScope.Session, CacheTtl = TimeSpan.FromMinutes(1) };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        var second = await assembler.BuildAsync(Request());

        Assert.Equal(2, contributor.Calls);
        Assert.Equal(ContextCacheStats.Empty, second.Cache);
    }

    [Fact]
    public async Task GlobalWithoutTtl_IsCalledEveryTime()
    {
        // 易变/按输入裁剪的全局块（时间块、按 UserInput 排序的能力清单）必须显式保持 TTL=0
        var contributor = new FakeContributor { CacheTtl = TimeSpan.Zero };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        var second = await assembler.BuildAsync(Request());

        Assert.Equal(2, contributor.Calls);
        Assert.Equal(ContextCacheStats.Empty, second.Cache);
    }

    [Fact]
    public async Task DifferentSubjects_DoNotShareCache()
    {
        var contributor = new FakeContributor { CacheTtl = TimeSpan.FromMinutes(1) };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request("builtin-rin"));
        await assembler.BuildAsync(Request("other-character"));

        Assert.Equal(2, contributor.Calls);   // 不同角色的人格完全不同，绝不能互借
    }

    [Fact]
    public async Task TtlExpiry_Recomputes()
    {
        var contributor = new FakeContributor { CacheTtl = TimeSpan.FromMilliseconds(1) };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        await Task.Delay(30);
        await assembler.BuildAsync(Request());

        Assert.Equal(2, contributor.Calls);
    }

    [Fact]
    public async Task Invalidate_ForcesRecompute()
    {
        // 宿主在"内容来源变了"时（配置热重载、人格文件被编辑）需要立刻生效，而不是等 TTL
        var contributor = new FakeContributor { CacheTtl = TimeSpan.FromMinutes(10) };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request("builtin-rin"));
        await assembler.BuildAsync(Request("builtin-rin"));
        Assert.Equal(1, contributor.Calls);

        assembler.Invalidate("other-subject");            // 只失效别的角色 → 这个仍命中
        await assembler.BuildAsync(Request("builtin-rin"));
        Assert.Equal(1, contributor.Calls);

        assembler.Invalidate("builtin-rin");              // 精确失效
        await assembler.BuildAsync(Request("builtin-rin"));
        Assert.Equal(2, contributor.Calls);

        assembler.Invalidate();                           // 全量失效
        await assembler.BuildAsync(Request("builtin-rin"));
        Assert.Equal(3, contributor.Calls);
    }

    [Fact]
    public async Task VersionProbe_ReusesEvenAfterTtlExpiry()
    {
        // 有探针时以**内容**为准：内容没变就复用，不受 TTL 限制
        var contributor = new FakeContributor
        {
            CacheTtl = TimeSpan.FromMilliseconds(1),
            VersionProbe = () => "v1",
        };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        await Task.Delay(30);
        var second = await assembler.BuildAsync(Request());

        Assert.Equal(1, contributor.Calls);
        Assert.Equal(2, contributor.Probes);          // 每回合都探一次（廉价）
        Assert.True(second.Cache.AnyHit);
    }

    [Fact]
    public async Task VersionProbe_ChangeRebuildsImmediatelyWithinTtl()
    {
        // 改了档案 → 下一回合立刻重建，不再等 TTL 到期（这是版本探针相对纯 TTL 的核心价值）
        var version = "v1";
        var contributor = new FakeContributor
        {
            CacheTtl = TimeSpan.FromMinutes(10),
            VersionProbe = () => version,
        };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        version = "v2";
        var second = await assembler.BuildAsync(Request());

        Assert.Equal(2, contributor.Calls);
        Assert.False(second.Cache.AnyHit);
    }

    [Fact]
    public async Task VersionProbe_NullFallsBackToTtl()
    {
        var contributor = new FakeContributor
        {
            CacheTtl = TimeSpan.FromMinutes(1),
            VersionProbe = () => null,
        };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        await assembler.BuildAsync(Request());

        Assert.Equal(1, contributor.Calls);           // 没有版本 → 按 TTL 命中
    }

    [Fact]
    public async Task VersionProbe_ThrowingIsTolerated()
    {
        // 探针失败（目录不可读等）不能让装配整体失败：退回 TTL 语义
        var contributor = new FakeContributor
        {
            CacheTtl = TimeSpan.FromMinutes(1),
            VersionProbe = () => throw new IOException("probe boom"),
        };
        var assembler = new DefaultContextAssembler([contributor]);

        await assembler.BuildAsync(Request());
        var second = await assembler.BuildAsync(Request());

        Assert.Equal(1, contributor.Calls);
        Assert.True(second.Cache.AnyHit);
    }
}
