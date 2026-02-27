using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.ToolUse;

public sealed class ToolUsePromptBuilder : IExecutorPromptBuilder<ToolUseInput>
{
    public const string DefaultSystemPrompt =
        """
你是一个角色回合工具调用决策器。你的任务是判断本轮是否需要调用一个工具，并输出严格 JSON。

你不是角色回复生成器。除非不需要工具，否则不要写最终角色对白。

规则：
- 如果计划明确需要工具，且工具目录中存在合适工具，输出 action=`call_tool`。
- 如果没有合适工具或工具调用已经完成，输出 action=`none`。
- 只能选择工具目录中存在的工具名。
- 不要虚构工具执行结果。
- JSON 字符串字段中的正文不要包含双引号 `"`，也不要包含中文引号 `"`。如果必须强调某个词，请改用 `''` 或 `````` 这样的标记方式。

只输出一个合法的 JSON 对象，不要输出 markdown 代码块围栏，不要输出额外解释。
JSON 结构：
{
  "action": "<call_tool|none>",
  "toolName": "<工具名，action=none 时为空>",
  "args": ["<位置参数>"],
  "options": {"<选项名>": "<选项值>"},
  "reason": "<简短原因>"
}
""";

    public ExecutorPrompt Build(ToolUseInput input)
    {
        var toolLines = input.AvailableTools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
            .Select(tool =>
            {
                var confirm = tool.NeedsConfirm ? ", needsConfirm=true" : string.Empty;
                return $"- {tool.Name}: {tool.Help}; usage={tool.Usage}; risk={tool.RiskLevel}{confirm}";
            })
            .ToList();

        var content =
            $"character_profile:\n{input.CharacterProfile.Trim()}\n\n" +
            $"scene_state:\n{input.SceneState.Trim()}\n\n" +
            $"current_goal:\n{input.CurrentGoal.Trim()}\n\n" +
            $"plan:\n{input.PlanSummary.Trim()}\n\n" +
            "available_tools:\n" +
            (toolLines.Count == 0 ? "- <empty>" : string.Join("\n", toolLines)) +
            "\n\nconversation:\n" +
            (input.Conversation.Count == 0 ? "- <empty>" : string.Join("\n", input.Conversation.Where(x => !string.IsNullOrWhiteSpace(x)))) +
            $"\n\nplayer_input:\n{input.PlayerInput.Trim()}\n\n" +
            $"last_tool_result:\n{input.LastToolResult.Trim()}";

        return new ExecutorPrompt
        {
            Messages =
            [
                new SystemMessage(string.IsNullOrWhiteSpace(input.SystemPromptOverride)
                    ? DefaultSystemPrompt
                    : input.SystemPromptOverride.Trim()),
                new ChatMessage(ChatMessageRole.User, content)
            ],
            Options = new LlmOptions
            {
                Temperature = input.Temperature,
                MaxTokens = input.MaxTokens,
                JsonMode = true
            }
        };
    }
}
