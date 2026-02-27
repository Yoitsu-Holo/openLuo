using openLuo.Modules.Agent.Application;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Interfaces.QQbot;

public sealed class QqBotReplyStyleChatHook : IAgentChatHookStage
{
    public QqBotReplyStyleChatHook()
    {
    }

    public Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default)
    {
        if (context.PresentationProfile != SessionPresentationProfile.InstantMessageCompact)
            return Task.FromResult(new AgentChatTurnBeforeResult());

        return Task.FromResult(new AgentChatTurnBeforeResult
        {
            ExtraContexts =
            [
                new AgentContextBlock(
                    EnhanceMessageRule.SafetyOrRuntimeRules,
                    """
                    当前输出通道是即时通讯聊天窗口。你的回复必须满足以下规则：
                    - 只输出角色真正会发送给对方的对话文本。
                    - 不要输出括号动作描写、舞台说明、旁白、镜头语言、心理活动或内心独白。
                    - 不要使用“（……）”“【……】”这类把心情、动作、环境单独写出来的格式。
                    - 除非用户明确要求长篇分析，否则优先给出相对简洁、自然、可直接发送的聊天回复。
                    - 回复重点放在角色实际说出口的话，不要额外补充她“心里怎么想”“表情如何变化”“尾巴/耳朵/身体动作如何反应”。
                    """)
            ]
        });
    }

    public Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnAfterResult());
}
