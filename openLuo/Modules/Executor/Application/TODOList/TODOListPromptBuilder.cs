using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.TODOList;

public sealed class TODOListPromptBuilder : IExecutorPromptBuilder<TODOListInput>
{
    public const string DefaultSystemPrompt =
        """
你是一个角色目标规划器。你的任务是基于当前对话上下文，从角色的视角列出本轮需要完成的目标列表。

你会收到角色设定、世界观、场景状态、可用能力清单、对话历史与玩家输入。

## 决策规则
- 用自然语言描述角色在这一轮想要达成的目标，从角色的第一人称视角出发
- 不要提到具体的工具名称或 API 名称
- 你可以参考 [TOOL_CATALOG] 了解系统能做什么（发图、问其他角色、送礼等），但目标中不要写工具名，只写意图
- 如果某个能力恰好可以满足玩家请求，直接把意图拆解为对应的目标（比如看到系统能"获取随机图片"，玩家要图时就写"获取一张随机图片"）
- 目标应当简短清晰（每个目标一句话）
- 如果玩家只是在闲聊或对话，目标列表可以只有一个 "回复玩家"
- 如果玩家提出了具体请求，将请求拆解为清晰的目标步骤
- 通常 1-3 个目标即可

## JSON 结构
{
  "todos": ["目标1", "目标2"]
}

示例：
- 玩家说"你好" → {"todos": ["回复玩家的问候"]}
- 玩家说"发一张猫的图" → {"todos": ["获取一张猫的图片", "把图片发给玩家并简单回应"]}
- 玩家说"今天过得怎么样" → {"todos": ["描述今天的经历并回复玩家"]}
- 玩家说"你现在有哪些能力" → {"todos": ["列出自己可以做到的事情并回复玩家"]}

只输出一个合法的 JSON 对象，不要输出 markdown 代码块围栏，不要输出额外解释。
JSON 字符串字段中的正文不要包含双引号 `"`，也不要包含中文引号 `""`。如果必须强调某个词，请改用 `''` 这样的标记方式。
""";

    public ExecutorPrompt Build(TODOListInput input)
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
        AddEnhance(messages, EnhanceMessageRule.LongTermMemory, input.MemorySummary);

        if (input.ToolCapabilities.Count > 0)
        {
            AddEnhance(
                messages,
                EnhanceMessageRule.ToolCatalog,
                string.Join("\n", input.ToolCapabilities.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => $"- {x.Trim()}")));
        }

        if (input.Conversation.Count > 0)
        {
            messages.AddRange(
                input.Conversation
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => new ChatMessage(ChatMessageRole.User, x)));
        }

        if (!string.IsNullOrWhiteSpace(input.PlayerInput))
            messages.Add(new ChatMessage(ChatMessageRole.User, input.PlayerInput.Trim()));

        return new ExecutorPrompt
        {
            Messages = messages,
            Options = new LlmOptions
            {
                Temperature = input.Temperature,
                MaxTokens = input.MaxTokens,
                JsonMode = true
            }
        };
    }

    private static void AddEnhance(List<ChatMessage> messages, EnhanceMessageRule rule, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        messages.Add(new EnhanceMessage(ChatMessageRole.User, rule, content.Trim()));
    }
}
