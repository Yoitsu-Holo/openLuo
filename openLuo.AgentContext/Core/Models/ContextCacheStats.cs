namespace openLuo.AgentContext.Core.Models;

/// <summary>
/// 本轮装配的全局块缓存账目（D46）。**可观测**：没有它就无法确认缓存真的在起作用
/// （"看起来更快了"不是证据），也无法在怀疑缓存吃掉了新内容时快速定位。
/// </summary>
public sealed record ContextCacheStats(int Hits, int Misses)
{
    /// <summary>未启用缓存统计时的初值。</summary>
    public static ContextCacheStats Empty { get; } = new(0, 0);

    /// <summary>本轮是否复用了全局块。</summary>
    public bool AnyHit => Hits > 0;

    public override string ToString() => $"hit={Hits} miss={Misses}";
}
