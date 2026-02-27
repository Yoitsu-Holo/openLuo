using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.StateUpdate;

public sealed class StateUpdatePromptBuilder : IExecutorPromptBuilder<StateUpdateInput>
{
    public const string DefaultSystemPrompt =
        """
你是一个角色状态结算器。你的任务是根据当前资源状态、玩家输入、角色回复与工具结果，输出结构化的状态变化建议。

你不能直接执行状态修改，只能输出 delta。

如果存在 inter-agent outcome，它表示内部协作返回的结构化事实、信号与建议。
你可以参考这些信息，但不能把建议当作已经执行成功的状态变更，也不能无根据夸大其含义。

你必须只输出一个合法的 JSON 对象，不要输出 markdown 代码块围栏，不要输出额外解释。
JSON 字符串字段中的正文不要包含双引号 `"`，也不要包含中文引号 `"`。如果必须强调某个词，请改用 `''` 这样的标记方式。
JSON 结构：
{
  "deltas": [
    {
      "resourceId": "<resource id>",
      "operation": "<add|set|mul|none>",
      "value": "<value>",
      "reason": "<原因>"
    }
  ],
  "reason": "<整体结算原因>",
  "confidence": 0.0
}
""";

    public ExecutorPrompt Build(StateUpdateInput input)
    {
        var toolResultLines = input.ToolResults
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => $"- {x.Trim()}")
            .ToList();

        var content =
            $"current_state:\n{input.CurrentStateSummary.Trim()}\n\n" +
            $"scene_state:\n{input.SceneState.Trim()}\n\n" +
            $"player_input:\n{input.PlayerInput.Trim()}\n\n" +
            $"character_response:\n{input.CharacterResponse.Trim()}\n\n" +
            "tool_results:\n" +
            (toolResultLines.Count == 0 ? "- <empty>" : string.Join("\n", toolResultLines));

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
