using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>固定限额提供者：测试、playground 与未接入配置的宿主使用。</summary>
public sealed class StaticContextLimitsProvider(ContextLimits limits) : IContextLimitsProvider
{
    /// <summary>使用 <see cref="ContextLimits.Default"/> 的单例。</summary>
    public static StaticContextLimitsProvider Default { get; } = new(ContextLimits.Default);

    public ContextLimits Current { get; } = limits;
}
