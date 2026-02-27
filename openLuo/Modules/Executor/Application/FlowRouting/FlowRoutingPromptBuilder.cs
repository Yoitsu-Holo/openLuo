using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.FlowRouting;

public sealed class FlowRoutingPromptBuilder : IExecutorPromptBuilder<FlowRoutingInput>
{
    public const string SystemPrompt =
        """
你是 Agent Flow 路由选择器。你的任务是在已通过程序性 guard 的候选边中选择下一条边。

你不能发明新的 edgeId 或 nodeId。
你不能执行工具、修改状态或输出角色对白。
如果候选边为空，输出空 selectedEdgeId 和 stopReason。

你必须只输出一个合法的 JSON 对象，不要输出 markdown 代码块围栏，不要输出额外解释。
JSON 字符串字段中的正文不要包含双引号 `"`，也不要包含中文引号 `"`。如果必须强调某个词，请改用 `''` 这样的标记方式。
JSON 结构：
{
  "selectedEdgeId": "<候选 edgeId 或空字符串>",
  "selectedNodeId": "<候选 toNodeId 或空字符串>",
  "confidence": 0.0,
  "reason": "<选择原因>",
  "stopReason": "<如果无法选择，填写原因，否则空字符串>"
}
""";

    public ExecutorPrompt Build(FlowRoutingInput input)
    {
        var candidateBlock = input.Candidates.Count == 0
            ? "- <empty>"
            : string.Join("\n", input.Candidates.Select(candidate =>
                $"- edgeId={candidate.EdgeId}; toNodeId={candidate.ToNodeId}; priority={candidate.Priority}; when={candidate.When}"));

        var userPrompt =
            $$"""
[FLOW_CONTEXT]
flowId: {{input.FlowId}}
currentNodeId: {{input.CurrentNodeId}}
[/FLOW_CONTEXT]

[PREVIOUS_NODE_OUTPUT]
{{NormalizeBlock(input.PreviousNodeOutput)}}
[/PREVIOUS_NODE_OUTPUT]

[FLOW_STATE]
{{NormalizeBlock(input.FlowStateSummary)}}
[/FLOW_STATE]

[CANDIDATE_EDGES]
{{candidateBlock}}
[/CANDIDATE_EDGES]
""";

        return new ExecutorPrompt
        {
            Messages =
            [
                new SystemMessage(string.IsNullOrWhiteSpace(input.SystemPromptOverride)
                    ? SystemPrompt
                    : input.SystemPromptOverride.Trim()),
                new ChatMessage(ChatMessageRole.User, userPrompt)
            ],
            Options = new LlmOptions
            {
                Temperature = input.Temperature,
                MaxTokens = input.MaxTokens,
                JsonMode = true
            }
        };
    }

    private static string NormalizeBlock(string value) =>
        string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
}
