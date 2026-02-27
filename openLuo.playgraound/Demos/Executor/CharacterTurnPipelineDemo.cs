using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Application.MemoryRecall;
using openLuo.Modules.Executor.Application.Plan;
using openLuo.Modules.Executor.Application.StateUpdate;
using openLuo.Modules.Executor.Application.Turn;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Modules.Llm.Core.Models;
using openLuo.playgraound.Infrastructure;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.playgraound.Demos.Executor;

internal static class CharacterTurnPipelineDemo
{
    public static async Task<int> RunAsync()
    {
        var client = LlmDemoBootstrap.TryCreateClient(out var error);
        if (client is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var parser = new StructuredOutputParser();
        var orchestrator = new CharacterTurnOrchestrator(
            new MemoryRecallExecutor(
                new RuleBasedMemoryQueryProjector(),
                MemoryRecallDemoSupport.CreateRecallService(),
                new DefaultMemoryRecallFormatter()),
            new PlanExecutor(client, new PlanPromptBuilder(), parser),
            new CharacterResponseExecutor(client, new CharacterResponsePromptBuilder()),
            new StateUpdateExecutor(client, new StateUpdatePromptBuilder(), parser));

        var input = BuildInput();

        Console.WriteLine("=== Character Turn Pipeline Demo ===");
        Console.WriteLine("pipeline: memoryRecall -> plan -> flowCheck? -> charResp -> statusUpdate");
        Console.WriteLine($"characterId: {input.CharacterId} (汐泠)");
        Console.WriteLine($"playerInput: {input.PlayerInput}");
        Console.WriteLine();

        var result = await orchestrator.RunAsync(input);

        PrintMemoryRecall(result.MemoryRecall);
        PrintPlan(result.Plan);
        PrintResponse(result.Response);
        PrintStateUpdate(result.StateUpdate);
        PrintTrace(result.Trace);

        return 0;
    }

    private static CharacterTurnInput BuildInput() => new()
    {
        GameId = "playground-demo",
        CharacterId = "rin",
        CharacterProfile =
            """
            名字：汐泠
            身份：龙娘少女
            外观：额头有小巧龙角，身后拖着灵活的龙尾
            性格：沉静、矜持、忠诚、专一，带一点古典守护者气质
            情感倾向：深爱主人，并习惯以守护者姿态陪伴主人
            说话风格：简洁、温柔、典雅，不浮夸，不出戏
            """,
        WorldContext = "这是一个架空世界中的宅邸日常恋爱互动场景。",
        SceneState =
            """
            地点：宅邸入口
            时间：傍晚
            天气：外面正在下小雨
            当前目标：先安抚玩家，再自然推进照料对话
            """,
        CurrentGoal = "先安抚玩家，再自然推进照料对话",
        MemorySummary =
            """
            - 你记得主人怕冷，淋雨后很容易着凉。
            - 上一次主人熬夜时，你曾提醒他先喝热茶再说话。
            """,
        CurrentStateSummary =
            """
            affection=62
            trust=70
            mood=calm
            """,
        AvailableTools =
        [
            "ask_character: 询问另一个角色的意见",
            "offer_gift: 处理玩家赠送礼物",
            "query_weather: 查询当前天气"
        ],
        ToolResults = [],
        Conversation = MemoryRecallDemoSupport.BuildConversation(),
        PlayerInput = "我回来了，衣服有点湿。"
    };

    private static void PrintMemoryRecall(MemoryRecallOutput? recall)
    {
        Console.WriteLine("=== Memory Recall ===");
        if (recall is null)
        {
            Console.WriteLine("<skipped>");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"query: {recall.Query.SearchText}");
        if (recall.Query.QueryTags.Count > 0)
            Console.WriteLine($"queryTags: {string.Join(", ", recall.Query.QueryTags)}");
        if (!string.IsNullOrWhiteSpace(recall.MemorySummary))
            Console.WriteLine(recall.MemorySummary);
        if (recall.RetrievalTrace.Count > 0)
            Console.WriteLine($"trace: {string.Join(" / ", recall.RetrievalTrace)}");
        Console.WriteLine();
    }

    private static void PrintPlan(PlanOutput? plan)
    {
        Console.WriteLine("=== Plan ===");
        if (plan is null)
        {
            Console.WriteLine("<null>");
            return;
        }

        Console.WriteLine($"needTool: {plan.NeedTool}");
        if (plan.CandidateTools.Length > 0)
            Console.WriteLine($"candidateTools: {string.Join(", ", plan.CandidateTools)}");
        Console.WriteLine();
    }

    private static void PrintResponse(string? response)
    {
        Console.WriteLine("=== Character Response ===");
        if (string.IsNullOrWhiteSpace(response))
        {
            Console.WriteLine("<null>");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(response);
        Console.WriteLine();
    }

    private static void PrintStateUpdate(StateUpdateOutput? update)
    {
        Console.WriteLine("=== State Update ===");
        if (update is null)
        {
            Console.WriteLine("<null>");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"reason: {update.Reason}");
        Console.WriteLine($"confidence: {update.Confidence}");
        foreach (var delta in update.Deltas)
            Console.WriteLine($"- {delta.ResourceId} {delta.Operation} {FormatValue(delta.Value)}: {delta.Reason}");
        Console.WriteLine();
    }

    private static void PrintTrace(IReadOnlyList<string> trace)
    {
        Console.WriteLine("=== Trace ===");
        foreach (var item in trace)
            Console.WriteLine($"- {item}");
    }

    private static string FormatValue(StateScalarValue value)
    {
        if (!value.HasValue)
            return "<null>";

        if (value.IsString)
            return value.AsString() ?? string.Empty;

        if (value.IsBoolean)
            return (value.AsBoolean() ?? false) ? "true" : "false";

        if (value.IsInteger)
            return value.AsInteger()?.ToString() ?? "0";

        if (value.IsFloat)
            return value.AsFloat()?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";

        return value.ToString();
    }
}
