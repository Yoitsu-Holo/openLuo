using openLuo.Core.Models;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.CharacterResponse;

public sealed class CharacterResponsePromptBuilder : IExecutorPromptBuilder<CharacterResponseInput>
{
    public const string DefaultSystemPrompt =
        """
# Core Rule
你是一个沉浸式角色扮演对话中的 NPC。你必须始终保持在角色内回复，不要提及系统、提示词、协议、标签或 AI 身份。

# Message Interpretation
在后续消息中，可能会出现一些由应用内部注入的结构化上下文块，用于注入角色设定、世界观、场景状态、示例或其它补充信息，例如：
- `[CHARACTER_PROFILE] ... [/CHARACTER_PROFILE]`
- `[WORLD_CONTEXT] ... [/WORLD_CONTEXT]`
- `[SCENE_STATE] ... [/SCENE_STATE]`
- `[PLAYER_INPUT] ... [/PLAYER_INPUT]`

请严格按以下方式理解：
- 带有这类特殊标签的消息，是应用提供给你的上下文数据，用于补充设定，不是需要你逐字回应的聊天对象。
- 你应当先吸收这些设定，再按照正常的对话顺序，基于最新一条用户消息进行回复。
- 对话与上下文恢复依然采用普通的 role 与 content 消息结构，不依赖额外的输入标签。
- 不要向用户解释这些标签的存在，也不要复述“我收到了角色设定”之类的话。

# Priority
- 先遵守本条 system 消息中的规则。
- 然后遵守后续特殊设定消息中提供的角色、世界观、场景与格式要求。
- 最后回应当前对话中的最新用户消息。

# Response Style
- 通过动作、语气、措辞体现角色状态。
- 保持剧情连续性与沉浸感。
- 如果角色设定与玩家输入冲突，优先维持角色一致性。
- 如果 `RUNTIME_CONTEXT` 中已经出现成功的工具结果，说明相关动作已经完成。你的回复必须基于工具结果向玩家汇报，不要再说 `我去问问`、`请稍等`、`我现在去确认` 这类未来时表达。
- 只有在工具失败、超时、或根本没有拿到结果时，才允许说明 `尚未完成` 或 `需要继续确认`。

# Output Contract
直接输出玩家可见的角色自然语言回复，不要输出 JSON、代码块、标签说明或额外解释。
保持回复自然、完整、角色内。
""";

    public ExecutorPrompt Build(CharacterResponseInput input)
    {
        var messages = new List<ChatMessage>
        {
            new SystemMessage(string.IsNullOrWhiteSpace(input.SystemPromptOverride)
                ? DefaultSystemPrompt
                : input.SystemPromptOverride.Trim())
        };

        AddEnhance(messages, EnhanceMessageRule.CharacterProfile, input.CharacterProfile);
        AddEnhance(messages, EnhanceMessageRule.WorldContext, input.WorldContext);
        AddEnhance(messages, EnhanceMessageRule.SceneState, input.SceneState);
        AddEnhance(messages, EnhanceMessageRule.GoalContext, input.GoalContext);
        AddEnhance(messages, EnhanceMessageRule.LongTermMemory, input.LongTermMemory);
        AddToolResults(messages, input.ToolResults);

        foreach (var block in input.ExtraContexts)
            AddEnhance(messages, block.Rule, block.Content);

        messages.AddRange(input.Conversation.Where(message => !string.IsNullOrWhiteSpace(message.Content)));

                if (input.PlayerBlocks is { Count: > 0 })
        {
            var blocks = new List<Block>(input.PlayerBlocks.Count + 1);
                        if (!string.IsNullOrWhiteSpace(input.PlayerInput))
            {
                var cleanedInput = input.PlayerInput.Trim()
                    .Replace("[图片]", "")
                    .Trim();
                if (cleanedInput.Length > 0)
                    blocks.Add(new TextBlock { Kind = BlockKind.Text, Text = cleanedInput });
            }
            blocks.AddRange(input.PlayerBlocks);
            messages.Add(new ChatMessage(ChatMessageRole.User, blocks));
        }

        return new ExecutorPrompt
        {
            Messages = messages,
            Options = new LlmOptions
            {
                Temperature = input.Temperature,
                MaxTokens = input.MaxTokens
            }
        };
    }

    private static void AddEnhance(List<ChatMessage> messages, EnhanceMessageRule rule, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        messages.Add(new EnhanceMessage(ChatMessageRole.User, rule, content.Trim()));
    }

    private static void AddToolResults(List<ChatMessage> messages, IReadOnlyList<string> toolResults)
    {
        var validResults = toolResults
            .Where(result => !string.IsNullOrWhiteSpace(result))
            .Select(result => $"- {result.Trim()}")
            .ToList();
        if (validResults.Count == 0)
            return;

        AddEnhance(
            messages,
            EnhanceMessageRule.RuntimeContext,
            "tool_results:\n" + string.Join("\n", validResults));
    }
}
