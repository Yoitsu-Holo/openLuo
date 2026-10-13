using openLuo.AgentContext.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 宿主限额来源（D44）：把配置映射为内核 <see cref="ContextLimits"/>。
/// <para>
/// 每次读 <see cref="Current"/> 都从静态配置门面取**当前快照**，因此
/// <c>config/agent.jsonc</c> 的热加载会立即作用到已存在的装配器与会话
/// （与 <c>HubStartup.BuildSecurityPolicy</c> 同一模式）。
/// </para>
/// </summary>
public sealed class ConfigContextLimitsProvider : IContextLimitsProvider
{
    public ContextLimits Current
    {
        get
        {
            var agent = openLuo.Infrastructure.Config.Config.Agent;
            return new ContextLimits
            {
                MaxTokens = Math.Max(1, agent.ContextTokenBudget),
                ConversationRetainCount = Math.Max(1, agent.ContextConversationRetainCount)
            };
        }
    }
}
