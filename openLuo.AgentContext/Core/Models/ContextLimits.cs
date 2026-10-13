namespace openLuo.AgentContext.Core.Models;

/// <summary>
/// 上下文窗口限额（D44）。唯一定义处，供装配器（系统块预算）与会话（对话窗口）共用；
/// 宿主从配置映射（<c>agent.contextTokenBudget</c> / <c>agent.contextConversationRetainCount</c>）。
/// </summary>
public sealed record ContextLimits
{
    /// <summary>系统块 + 对话历史的 token 上限（估算口径见 <see cref="ITokenCounter"/>）。</summary>
    public int MaxTokens { get; init; } = 24_000;

    /// <summary>单回合从对话存储读取的最大历史条数（先按条数取，再按剩余预算裁剪）。</summary>
    public int ConversationRetainCount { get; init; } = 24;

    /// <summary>未接入配置时的默认（与 <c>agent.contextTokenBudget</c> / <c>agent.contextConversationRetainCount</c> 默认一致）。</summary>
    public static ContextLimits Default { get; } = new();
}

/// <summary>
/// 限额来源端口（D44）。取 <see cref="Current"/> 而非在构造期取值，
/// 使配置热加载对长生命周期对象（装配器/会话）立即生效。
/// </summary>
public interface IContextLimitsProvider
{
    ContextLimits Current { get; }
}
