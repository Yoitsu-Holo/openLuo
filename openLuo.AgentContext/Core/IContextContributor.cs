using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Core;

/// <summary>
/// 上下文贡献者（D42/D43）。每轮快照构造时被串行调用；原子独立、只读查询、
/// 失败独立降级（返回 Unavailable 结构化状态，不阻塞其他贡献）。
/// </summary>
public interface IContextContributor
{
    /// <summary>贡献者唯一标识，如 "memory" / "world:state" / "companion:identity"。</summary>
    string Id { get; }

    /// <summary>
    /// 本贡献者产出内容所属的域（D45）。**默认 <see cref="ContextScope.Session"/>**——既有实现不改也保持旧行为；
    /// 声明 Global 者表示"内容只与角色有关、所有会话共用"（身份/persona/规则/时间/能力清单），
    /// 声明 User 者表示"内容与人有关、跨会话"（长期记忆）。
    /// </summary>
    ContextScope Scope => ContextScope.Session;

    /// <summary>
    /// 全局块的可复用时长（D46）：&gt;0 表示在 TTL 内**直接复用上次结果、不再调用本贡献者**；
    /// <see cref="TimeSpan.Zero"/>（默认）= 每回合重算。
    /// <para>
    /// **只对 <see cref="ContextScope.Global"/> 生效**——Session/User 域的内容必须每回合新鲜。
    /// 易变内容（如时间块）必须保持 0：给它 TTL 等于把内容冻结在缓存里。
    /// 复用是**引用同一批不可变贡献**（不复制），所以同一角色的所有会话共享同一份全局块。
    /// </para>
    /// <para>
    /// 与 <see cref="CurrentVersion"/> 的关系：有版本探针时以**版本**为准（内容变了立刻重建，不受 TTL 限制），
    /// TTL 退化为"探针拿不到版本时"的兜底。
    /// </para>
    /// </summary>
    TimeSpan CacheTtl => TimeSpan.Zero;

    /// <summary>
    /// 廉价的**内容版本探针**（D46）：返回 null 表示"没有探针，按 <see cref="CacheTtl"/> 处理"。
    /// <para>
    /// 存在的意义：让缓存**按内容失效**而不是按时间——例如人格档案的探针只 stat 一下目录里文件的
    /// mtime/size（不解析），于是"改了档案 → 下一回合立刻生效"，既没有 TTL 窗口期的陈旧，
    /// 也不必让宿主去猜何时该 <see cref="IContextGlobalCache.Invalidate"/>。
    /// 实现必须是**廉价**的：探针每回合都会被调用，贵过重新构建内容就没有意义了。
    /// </para>
    /// </summary>
    string? CurrentVersion(ContextBuildRequest request) => null;

    Task<ContextContributionResult> ContributeAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// 全局块缓存的失效入口（D46）。宿主在"内容来源变了"时调用（例如配置热加载、人格文件被编辑），
/// 让下一次装配立刻重算，而不用等 TTL 到期。
/// </summary>
public interface IContextGlobalCache
{
    /// <summary>失效某个 subject 的全局块；传 null 失效全部。</summary>
    void Invalidate(string? subjectId = null);
}
