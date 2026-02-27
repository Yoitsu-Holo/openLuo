using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.playgraound.Infrastructure;

namespace openLuo.playgraound.Demos.Executor;

internal static class FlowRoutingExecutorDemo
{
    public static async Task<int> RunAsync()
    {
        var client = LlmDemoBootstrap.TryCreateClient(out var error);
        if (client is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var executor = new FlowRoutingExecutor(
            client,
            new FlowRoutingPromptBuilder(),
            new StructuredOutputParser());

        var input = BuildInput();

        Console.WriteLine("=== Flow Routing Executor Demo ===");
        Console.WriteLine("purpose: choose one next edge from guarded candidates");
        Console.WriteLine($"flowId: {input.FlowId}");
        Console.WriteLine($"currentNodeId: {input.CurrentNodeId}");
        Console.WriteLine();
        Console.WriteLine("previousNodeOutput:");
        Console.WriteLine(input.PreviousNodeOutput);
        Console.WriteLine();
        Console.WriteLine("candidates:");
        foreach (var candidate in input.Candidates)
            Console.WriteLine($"- {candidate.EdgeId} -> {candidate.ToNodeId} | priority={candidate.Priority} | when={candidate.When}");
        Console.WriteLine();

        var result = await executor.ExecuteAsync(input);

        Console.WriteLine("=== Result ===");
        Console.WriteLine($"success: {result.Success}");
        if (!result.Success)
        {
            Console.WriteLine($"error: {result.Error}");
            Console.WriteLine("raw:");
            Console.WriteLine(result.RawOutput);
            return 1;
        }

        var output = result.Output;
        if (output is null)
        {
            Console.WriteLine("<null>");
            return 1;
        }

        Console.WriteLine($"selectedEdgeId: {output.SelectedEdgeId}");
        Console.WriteLine($"selectedNodeId: {output.SelectedNodeId}");
        Console.WriteLine($"confidence: {output.Confidence}");
        Console.WriteLine($"reason: {output.Reason}");
        if (!string.IsNullOrWhiteSpace(output.StopReason))
            Console.WriteLine($"stopReason: {output.StopReason}");

        Console.WriteLine();
        Console.WriteLine("raw:");
        Console.WriteLine(result.RawOutput);

        return 0;
    }

    private static FlowRoutingInput BuildInput() => new()
    {
        FlowId = "character.standard_chat",
        CurrentNodeId = "plan",
        PreviousNodeOutput =
            """
            {
              "intent": "tool_use",
              "nextPhase": "toolUse",
              "needTool": true,
              "candidateTools": ["offer_gift"],
              "planningNotes": [
                "玩家明确表达将银铃送给角色",
                "角色不能直接宣称已经收下礼物，必须先进入工具调用验证库存和执行副作用"
              ]
            }
            """,
        FlowStateSummary =
            """
            当前是角色对话回合。
            Agent 已完成 memoryRecall 和 plan。
            程序性 guard 已过滤掉不可达边，下面候选边都允许进入。
            """,
        Candidates =
        [
            new FlowRoutingCandidate
            {
                EdgeId = "plan-to-tool-use",
                ToNodeId = "toolUse",
                Priority = 100,
                When = "当计划结果要求调用工具，或角色回复前必须先验证/执行副作用时，进入工具调用节点。"
            },
            new FlowRoutingCandidate
            {
                EdgeId = "plan-to-character-response",
                ToNodeId = "characterResponse",
                Priority = 50,
                When = "当本轮不需要工具、副作用或跨角色询问，可以直接生成角色回复时，进入角色回复节点。"
            },
            new FlowRoutingCandidate
            {
                EdgeId = "plan-to-abort",
                ToNodeId = "abort",
                Priority = 0,
                When = "当计划结果无法执行、输入不安全、或没有任何合理下一步时，中止当前 flow。"
            }
        ]
    };
}
