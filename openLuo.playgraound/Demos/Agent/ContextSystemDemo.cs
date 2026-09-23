using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Application.TODOList;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.playgraound.Demos.Agent;

internal static class ContextSystemDemo
{
    public static async Task<int> RunAsync()
    {
        var manager = CreateManager();
        var compiler = new AgentExecutorContextCompiler(manager, new PassThroughAssetImageResolver());
        var turn = BuildTurnContext();

        var todoInput = compiler.BuildTodoListInput(turn, temperature: 0.1f, maxTokens: 128);
        Expect(todoInput.CharacterProfile.Contains("汐泠", StringComparison.Ordinal), "TODO context contains character profile");
        Expect(todoInput.Conversation.Any(line => line.Contains("[图片]", StringComparison.Ordinal)), "TODO context converts history image to marker");
        Expect(todoInput.ToolCapabilities.All(line => !line.Contains("narrative_chat", StringComparison.OrdinalIgnoreCase)), "TODO context hides narrative_chat planning tool");
        Expect(todoInput.PlayerBlocks is { Count: 0 }, "TODO context has no current raw blocks when current input is text-only");

        var responseInput = await compiler.BuildCharacterResponseInputAsync(turn, new AgentToolUseResult(), temperature: 0.2f, maxTokens: 256);
        var userConversation = responseInput.Conversation.Single(message => message.Role == ChatMessageRole.User);
        var assistantConversation = responseInput.Conversation.Single(message => message.Role == ChatMessageRole.Assistant);
        Expect(userConversation.Blocks?.Any(block => block is ImageBlock { Source: BlockSource.User }) == true, "response context preserves user image blocks");
        Expect(assistantConversation.Blocks?.Any(block => block is ImageBlock { Source: BlockSource.Agent }) == true, "response context preserves agent image blocks (ObservableBlocks media mode)");

        // exec 循环的 ToolMessages 通道（Phase 4.5）：工具文本并入 ToolResults、媒体块并入对话。
        var toolImage = new ImageBlock
        {
            Kind = BlockKind.Image,
            Source = BlockSource.Agent,
            AssetId = "asset://tool-output",
            MimeType = "image/png",
            DataUri = "data:image/png;base64,dG9vbC1pbWFnZQ=="
        };
        var toolResult = new AgentToolUseResult
        {
            ToolMessages =
            [
                new ChatMessage(ChatMessageRole.Tool, "demo_generate_image: success") { ToolCallId = "t1", Blocks = [toolImage] }
            ]
        };
        var toolResponseInput = await compiler.BuildCharacterResponseInputAsync(turn, toolResult, temperature: 0.2f, maxTokens: 256);
        Expect(toolResponseInput.ToolResults.Any(content => content.Contains("demo_generate_image: success")), "tool message content merged into ToolResults");
        Expect(toolResponseInput.Conversation.Any(message => message.Blocks?.Any(block => block is ImageBlock) == true), "tool media block merged into response conversation");
        Expect(!toolResponseInput.RequestVision, "RequestVision gated off when no vision-capable route configured");

        Console.WriteLine("=== Context System Demo ===");
        Console.WriteLine("model: not called");
        Console.WriteLine("flow: CharacterTurnContext -> AgentContextManager -> AgentExecutorContextCompiler");
        Console.WriteLine();
        Console.WriteLine($"todo.playerInput: {todoInput.PlayerInput}");
        Console.WriteLine($"todo.conversation: {string.Join(" | ", todoInput.Conversation)}");
        Console.WriteLine($"todo.tools: {string.Join(" | ", todoInput.ToolCapabilities)}");
        Console.WriteLine($"response.userBlocks: {userConversation.Blocks?.Count ?? 0}");
        Console.WriteLine($"response.assistantBlocks: {assistantConversation.Blocks?.Count ?? 0}");
        Console.WriteLine($"toolMessages.toolResults: {string.Join(" | ", toolResponseInput.ToolResults)}");
        Console.WriteLine($"toolMessages.requestVision: {toolResponseInput.RequestVision}");

        return 0;
    }

    private static IAgentContextManager CreateManager()
    {
        var manager = new AgentContextManager(
        [
            new TodoListContextRegistration(),
            new CharacterResponseContextRegistration(),
            new StateUpdateContextRegistration()
        ]);
        return manager;
    }

    private static CharacterTurnContext BuildTurnContext()
    {
        var userImage = new ImageBlock
        {
            Kind = BlockKind.Image,
            Source = BlockSource.User,
            AssetId = "asset://player-upload",
            MimeType = "image/png",
            DataUri = "data:image/png;base64,dXNlci1pbWFnZQ=="
        };
        var agentImage = new ImageBlock
        {
            Kind = BlockKind.Image,
            Source = BlockSource.Agent,
            AssetId = "asset://agent-output",
            MimeType = "image/png",
            DataUri = "data:image/png;base64,YWdlbnQtaW1hZ2U="
        };

        return new CharacterTurnContext
        {
            Request = new CharacterTurnRequest
            {
                Context = new AgentContext
                {
                    GameId = "playground-context-demo",
                    CharacterId = "rin"
                },
                Profile = new AgentProfile
                {
                    CharacterId = "rin",
                    DisplayName = "汐泠"
                },
                Message = new AgentMessage(
                    MessageId: "msg-current",
                    GameId: "playground-context-demo",
                    From: "player",
                    To: "rin",
                    Type: AgentMessageType.Chat,
                    Payload: "继续说说上面的图",
                    CorrelationId: "turn-context-demo",
                    TimestampUtc: DateTimeOffset.UtcNow),
                Memory = new CharacterMemorySnapshot()
            },
            Profile = new CharacterAgentProfile
            {
                CharacterId = "rin",
                DisplayName = "汐泠",
                RolePrompt = "你是温柔、轻快的陪伴型角色。"
            },
            State = new CharacterAgentState(),
            Memory = new CharacterMemorySnapshot { Summary = "玩家喜欢雨天后喝热茶。" },
            CurrentStateSummary = "trust=42, energy=80",
            CapabilitySnapshot = new AgentCapabilitySnapshot
            {
                Capabilities =
                [
                    new AgentCapabilityDescriptor
                    {
                        Name = "character_response",
                        HelpShort = "生成角色自然回复",
                        Usage = "character_response"
                    },
                    new AgentCapabilityDescriptor
                    {
                        Name = "narrative_chat",
                        HelpShort = "内部叙事渲染链",
                        Usage = "narrative_chat --message <text>"
                    }
                ]
            },
            PromptContext = new CharacterPromptContext
            {
                CharacterProfile = "角色：汐泠 / 轻快、可靠、会主动照顾玩家情绪。",
                WorldContext = "世界：开放式陪伴运行时。",
                SceneState = "场景：玩家刚发来一张图片。",
                GoalContext = "目标：理解玩家意图并自然回应。",
                PlayerInput = "继续说说上面的图",
                Conversation =
                [
                    new AgentConversationMessage(AgentConversationRole.User, "这张图片里有什么？",
                    [
                        new TextBlock { Kind = BlockKind.Text, Source = BlockSource.User, Text = "这张图片里有什么？" },
                        userImage
                    ]),
                    new AgentConversationMessage(AgentConversationRole.Assistant, "我刚生成了一张参考图。",
                    [
                        new TextBlock { Kind = BlockKind.Text, Source = BlockSource.Agent, Text = "我刚生成了一张参考图。" },
                        agentImage
                    ])
                ]
            }
        };
    }

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException($"ContextSystemDemo failed: {description}");
        Console.WriteLine($"ok: {description}");
    }

    private sealed class PassThroughAssetImageResolver : IAssetImageResolver
    {
        public Task<IReadOnlyList<Block>> ResolveAsync(IReadOnlyList<Block>? blocks, CancellationToken ct = default) =>
            Task.FromResult(blocks ?? []);
    }
}
