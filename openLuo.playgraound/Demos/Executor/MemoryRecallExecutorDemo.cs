using openLuo.Modules.Executor.Application.MemoryRecall;

namespace openLuo.playgraound.Demos.Executor;

internal static class MemoryRecallExecutorDemo
{
    public static async Task<int> RunAsync()
    {
        var executor = new MemoryRecallExecutor(
            new RuleBasedMemoryQueryProjector(),
            MemoryRecallDemoSupport.CreateRecallService(),
            new DefaultMemoryRecallFormatter());

        var input = new MemoryRecallInput
        {
            GameId = "playground-demo",
            CharacterId = "rin",
            SceneState =
                """
                地点：宅邸入口
                时间：傍晚
                天气：外面正在下小雨
                当前目标：先安抚玩家，再自然推进照料对话
                """,
            CurrentGoal = "先安抚玩家，再自然推进照料对话",
            RecentConversation = MemoryRecallDemoSupport.BuildConversation(),
            PlayerInput = "我回来了，衣服有点湿。",
            Options = new MemoryRecallOptions
            {
                TopK = 5,
                MaxSnippetCount = 3,
                IncludeCharacterPrivateMemory = true,
                IncludeSharedMemory = false
            }
        };

        var result = await executor.ExecuteAsync(input);

        Console.WriteLine("=== Memory Recall Executor Demo ===");
        Console.WriteLine($"success: {result.Success}");
        if (!result.Success || result.Output is null)
        {
            Console.WriteLine($"error: {result.Error}");
            return 1;
        }

        var output = result.Output;
        Console.WriteLine($"searchText: {output.Query.SearchText}");
        Console.WriteLine($"queryTags: {string.Join(", ", output.Query.QueryTags)}");
        Console.WriteLine();
        Console.WriteLine("summary:");
        Console.WriteLine(output.MemorySummary);
        Console.WriteLine();
        Console.WriteLine("snippets:");
        foreach (var snippet in output.MemorySnippets)
            Console.WriteLine($"- {snippet.MemoryId}: {snippet.Summary}");
        Console.WriteLine();
        Console.WriteLine($"trace: {string.Join(" / ", output.RetrievalTrace)}");

        return 0;
    }
}
