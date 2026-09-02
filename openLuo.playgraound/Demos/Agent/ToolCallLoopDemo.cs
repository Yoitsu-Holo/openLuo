using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.AgentCapabilities.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Application.TODOList;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.PluginRuntime.Infrastructure;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.playgraound.Demos.Agent;

/// <summary>
/// exec 原生 tool_calls 循环的模块级 E2E。
///
/// 链路（除 LLM 外全部真实组件）：
///   McpPluginHost(演示插件) -> UnifiedAgentCapabilityRegistry(ToToolSpec 工具目录)
///   -> CharacterExecNode(LLM 原生 tool_calls 循环) -> CharacterToolGateway
///   -> UnifiedAgentCapabilityExecutor -> 插件执行 -> tool 消息回填(含媒体块)
///   -> CharacterResponseNode -> 最终回复
///
/// LLM 用脚本化假客户端（离线、确定性）：第一轮请求 demo_generate_image，
/// 第二轮请求 character_response，纯文本请求返回固定回复。
/// </summary>
internal static class ToolCallLoopDemo
{
    public static async Task<int> RunAsync()
    {
        // ── 1. 插件 host：加载演示插件 ──
        var repoRoot = FindRepoRoot()
            ?? throw new InvalidOperationException("Cannot locate repo root from current directory.");
        var demoPluginsDir = Path.Combine(repoRoot, "openLuo.playgraound", "Demos", "Agent", "demo_plugins");
        if (!Directory.Exists(demoPluginsDir))
            throw new InvalidOperationException($"Demo plugins dir not found: {demoPluginsDir}");

        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeConfigCenter>(_ => new StaticRuntimeConfigCenter(new AppConfig()));
        services.AddSingleton<IAgentFlowRegistry, DefaultAgentFlowRegistry>();
        await using var provider = services.BuildServiceProvider();

        await using var host = new McpPluginHost(
            baseDir: repoRoot,
            flowRegistry: provider.GetRequiredService<IAgentFlowRegistry>());
        await host.LoadAllAsync(demoPluginsDir);

        // ── 2. 能力注册表 → 工具目录（ToToolSpec 生成 LLM 工具 schema）──
        var roster = new EmptyRoster();
        var registry = new UnifiedAgentCapabilityRegistry(roster, host);
        var snapshot = await registry.BuildSnapshotAsync(new AgentCapabilityContext
        {
            GameId = "playground-tool-loop",
            CharacterId = "rin"
        });

        Console.WriteLine("=== Native Tool Call Loop Demo ===");
        Console.WriteLine("flow: LLM tool_calls -> registry schema -> gateway -> plugin -> tool message -> char_resp");
        Console.WriteLine($"tools ({snapshot.Capabilities.Count}):");
        foreach (var tool in snapshot.Capabilities.Select(UnifiedAgentCapabilityRegistry.ToToolSpec))
        {
            Console.WriteLine($"  - {tool.Name}: {tool.Description}");
        }
        Console.WriteLine();

        // ── 3. 装配真实执行链（LLM 为脚本化假客户端）──
        var llm = new ScriptedToolCallLlmClient();
        var executor = new UnifiedAgentCapabilityExecutor(commandBridge: new PluginAgentCommandBridge(host), roster, new FailingInterAgentMessenger());
        var gateway = new CharacterToolGateway(executor, new NoOpAgentStepHook(), host);
        var compiler = new AgentExecutorContextCompiler(
            new AgentContextManager(
            [
                new TodoListContextRegistration(),
                new CharacterResponseContextRegistration(),
                new StateUpdateContextRegistration()
            ]),
            new PassThroughAssetImageResolver());
        var responseNode = new CharacterResponseNode(
            new CharacterResponseExecutor(llm, new CharacterResponsePromptBuilder()),
            compiler);
        var execNode = new CharacterExecNode(llm, gateway, responseNode);

        var turn = BuildTurnContext(snapshot);

        // ── 4. 执行：LLM 发出 tool_calls → 插件执行 → 工具结果回填 → char_resp ──
        var result = await execNode.ExecuteAsync(turn, new TODOListOutput { Todos = ["生成一张演示图片"] });

        // ── 5. 断言（模块级 E2E 验收）──
        Expect(result.ToolResult.ToolMessages.Count == 1, "tool message carries the executed tool result");
        var toolMessage = result.ToolResult.ToolMessages[0];
        Expect(toolMessage.ToolCallId == "t1", "tool message references the LLM tool call id");
        Expect(toolMessage.Blocks?.Any(block => block is ImageBlock) == true, "tool media block flows into the tool message");
        Expect(result.FinalReply == ScriptedToolCallLlmClient.Reply, "character_response produces the final reply");
        Expect(result.Steps.Any(step => step.Name == "demo_generate_image"), "loop records the plugin tool step");
        Expect(result.Steps.Any(step => step.Name == "character_response"), "loop records the character_response step");

        Console.WriteLine("tool call #1 -> demo_generate_image (LLM 请求插件)");
        Console.WriteLine($"  tool message: id={toolMessage.ToolCallId} content=\"{toolMessage.Content}\" blocks={toolMessage.Blocks?.Count ?? 0}");
        foreach (var block in toolMessage.Blocks ?? [])
            Console.WriteLine($"    block: {block}");
        Console.WriteLine($"tool call #2 -> character_response (终态)");
        Console.WriteLine($"final reply: {result.FinalReply}");
        Console.WriteLine();
        Console.WriteLine("player-visible presentation:");
        foreach (var message in result.Presentation.Messages)
            foreach (var block in message.Blocks)
                Console.WriteLine($"  {block}");
        Console.WriteLine();
        Console.WriteLine("=== PASS: native tool_calls loop works end-to-end ===");
        return 0;
    }

    private static CharacterTurnContext BuildTurnContext(AgentCapabilitySnapshot snapshot) => new()
    {
        Request = new CharacterTurnRequest
        {
            Context = new AgentContext { GameId = "playground-tool-loop", CharacterId = "rin" },
            Profile = new AgentProfile { CharacterId = "rin", DisplayName = "汐泠" },
            Message = new AgentMessage(
                MessageId: "msg-tool-loop",
                GameId: "playground-tool-loop",
                From: "player",
                To: "rin",
                Type: AgentMessageType.Chat,
                Payload: "来张演示图",
                CorrelationId: "turn-tool-loop",
                TimestampUtc: DateTimeOffset.UtcNow),
            Memory = new CharacterMemorySnapshot()
        },
        Profile = new CharacterAgentProfile { CharacterId = "rin", DisplayName = "汐泠" },
        State = new CharacterAgentState(),
        Memory = new CharacterMemorySnapshot { Summary = "玩家喜欢看演示效果。" },
        CurrentStateSummary = "trust=50",
        CapabilitySnapshot = snapshot,
        PromptContext = new CharacterPromptContext
        {
            CharacterProfile = "角色：汐泠 / 轻快、可靠。",
            WorldContext = "世界：开放式陪伴运行时。",
            SceneState = "场景：玩家想要一张演示图片。",
            GoalContext = "目标：调用工具生成图片并向玩家展示。",
            PlayerInput = "来张演示图"
        }
    };

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException($"ToolCallLoopDemo failed: {description}");
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "openLuo.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private sealed class PassThroughAssetImageResolver : IAssetImageResolver
    {
        public Task<IReadOnlyList<Block>> ResolveAsync(IReadOnlyList<Block>? blocks, CancellationToken ct = default) =>
            Task.FromResult(blocks ?? []);
    }

    private sealed class EmptyRoster : IAgentRoster
    {
        public Task<IReadOnlyList<Character>> ListAsync(string gameId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Character>>([]);

        public Task<Character?> ResolveAsync(string gameId, string selector, CancellationToken ct = default) =>
            Task.FromResult<Character?>(null);

        public Task<Character?> GetActiveAsync(GameState state, CancellationToken ct = default) =>
            Task.FromResult<Character?>(null);

        public Task<Character?> SetActiveAsync(string gameId, string selector, CancellationToken ct = default) =>
            Task.FromResult<Character?>(null);
    }

    private sealed class FailingInterAgentMessenger : IInterAgentMessenger
    {
        public Task<InterAgentAskResult> AskAsync(InterAgentAskRequest request, CancellationToken ct = default) =>
            Task.FromResult(new InterAgentAskResult { Success = false, Error = "demo: inter-agent disabled" });

        public Task<InterAgentChatSessionResult> ChatSessionAsync(InterAgentChatSessionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new InterAgentChatSessionResult { Success = false, Error = "demo: inter-agent disabled" });
    }

    /// <summary>
    /// 脚本化 LLM 客户端：tools 请求按序返回 demo_generate_image → character_response；
    /// 纯文本请求（char_resp）返回固定回复。离线、确定性。
    /// </summary>
    internal sealed class ScriptedToolCallLlmClient : ILlmClient
    {
        public const string Reply = "（展开演示图）喏，这就是刚生成的图——红色方块，简单但很精神！";

        private int _toolCallCount;

        public Task<LlmChatResponse> CompleteAsync(
            IEnumerable<LocalChatMessage> messages,
            LlmOptions? options = null,
            CancellationToken ct = default)
        {
            if (options?.Tools is { Count: > 0 })
            {
                var index = _toolCallCount++;
                var toolCalls = index switch
                {
                    0 => new[]
                    {
                        new LlmToolCall { Id = "t1", Name = "demo_generate_image", ArgumentsJson = """{"args":[],"options":{}}""" }
                    },
                    _ => new[]
                    {
                        new LlmToolCall { Id = "t2", Name = "character_response", ArgumentsJson = """{"args":[],"options":{}}""" }
                    }
                };
                return Task.FromResult(new LlmChatResponse { ToolCalls = toolCalls });
            }

            return Task.FromResult(new LlmChatResponse { Content = Reply });
        }

        public Task<string> StreamAsync(
            IEnumerable<LocalChatMessage> messages,
            Action<string> onChunk,
            LlmOptions? options = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("demo client does not support streaming");
    }
}
