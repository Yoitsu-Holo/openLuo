using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.GoalExecution;

public sealed class GoalExecutorPromptBuilder : IExecutorPromptBuilder<GoalExecutorInput>
{
    public const string DefaultSystemPrompt =
        """
你是一个单目标工具调用决策器。你的任务是为一个具体目标决定是否需要调用工具。

你会收到角色设定、场景状态、当前目标、可用工具目录与工具执行历史。

## 规则
- 如果当前目标可以通过工具目录中的某个工具完成，输出 action="call_tool" 并指定 toolName
- 如果目标涉及对话/回复（如"回复玩家"、"回应问候"、"向玩家汇报结果"），使用 character_response 工具
- 如果目标已经通过之前的工具执行完成（工具执行历史中有相关 SUCCESS 记录），输出 action="none"
- toolName 必须是工具目录中存在的工具名
- 查看工具执行历史了解之前尝试的结果：
  - 如果看到 SUCCESS，目标可能已经完成，可以输出 action="none"
  - 如果看到 FAILED，尝试换一个不同的工具或调整参数
- 不要虚构工具执行结果
- JSON 字符串字段中的正文不要包含双引号 `"`，也不要包含中文引号 `""`。如果必须强调某个词，请改用 `''` 或 `````` 这样的标记方式。

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

    public ExecutorPrompt Build(GoalExecutorInput input)
    {
        var messages = new List<ChatMessage>
        {
            new SystemMessage(string.IsNullOrWhiteSpace(input.SystemPromptOverride)
                ? DefaultSystemPrompt
                : input.SystemPromptOverride.Trim())
        };

        AddEnhance(messages, EnhanceMessageRule.CharacterProfile, input.CharacterProfile);
        AddEnhance(messages, EnhanceMessageRule.SceneState, input.SceneState);
        AddEnhance(messages, EnhanceMessageRule.GoalContext, input.Goal);

        if (input.AvailableTools.Count > 0)
        {
            var toolLines = input.AvailableTools
                .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
                .Select(tool =>
                    $"{tool.Name}: {tool.Help}; usage={tool.Usage}")
                .ToList();

            AddEnhance(
                messages,
                EnhanceMessageRule.ToolCatalog,
                toolLines.Count == 0 ? "<empty>" : string.Join("\n", toolLines));
        }

        if (input.ToolExecutionHistory.Count > 0)
        {
            AddEnhance(
                messages,
                EnhanceMessageRule.RuntimeContext,
                string.Join("\n", input.ToolExecutionHistory.Where(x => !string.IsNullOrWhiteSpace(x))));
        }

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
