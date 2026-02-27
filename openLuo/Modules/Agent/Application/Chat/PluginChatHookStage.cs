using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public sealed class PluginChatHookStage(
    IPluginHost pluginHost,
    IGameLogger logger) : IAgentChatHookStage
{
    public async Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default)
    {
        try
        {
            var result = await pluginHost.CallHookAsync("onChatBefore", new
            {
                gameId = context.GameId,
                characterId = context.TargetCharacter.Id,
                archetypeId = context.TargetCharacter.ArchetypeId,
                playerMessage = context.PlayerMessage,
                presentationProfile = context.PresentationProfile.ToString(),
                context = new
                {
                    systemPrompt = string.Empty
                }
            }, ct);

            var extraContexts = new List<AgentContextBlock>();
            if (!string.IsNullOrWhiteSpace(result.SystemPrompt))
            {
                extraContexts.Add(new AgentContextBlock(
                    EnhanceMessageRule.WorldContext,
                    result.SystemPrompt.Trim()));
            }

            return new AgentChatTurnBeforeResult
            {
                ExtraContexts = extraContexts
            };
        }
        catch (Exception ex)
        {
            logger.Warn("chat/hook", $"plugin onChatBefore skipped: {ex.Message}");
            return new AgentChatTurnBeforeResult();
        }
    }

    public Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnAfterResult());
}
