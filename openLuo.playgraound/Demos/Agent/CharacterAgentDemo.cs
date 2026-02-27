using Microsoft.Extensions.DependencyInjection;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.AgentCapabilities.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Application.GiftIntent;
using openLuo.Modules.Executor.Application.Plan;
using openLuo.Modules.Executor.Application.PlannedExecution;
using openLuo.Modules.Executor.Application.StateUpdate;
using openLuo.Modules.Executor.Application.ToolUse;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.InterAgent.Application;
using openLuo.Modules.InterAgent.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Modules.SessionRuntime.Application;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.playgraound.Infrastructure;

namespace openLuo.playgraound.Demos.Agent;

internal static class CharacterAgentDemo
{
    public static async Task<int> RunAsync()
    {
        var client = LlmDemoBootstrap.TryCreateClient(out var error);
        if (client is null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var services = BuildServices(client);
        await using var provider = services.BuildServiceProvider();

        await SeedContextsAsync(provider);

        var runtimeHub = provider.GetRequiredService<IAgentRuntimeHub>();
        await runtimeHub.EnsurePartyStartedAsync("playground-agent-demo");

        var character = DemoCharacters.All.Single(x => x.Id == "rin");
        var turnContext = new AgentChatTurnContext
        {
            GameId = "playground-agent-demo",
            TargetCharacter = character,
            State = new GameState
            {
                Id = "playground-agent-demo",
                ActiveCharacterId = "rin",
                CurrentLocation = "宅邸客厅",
                CurrentDay = 1,
                CurrentMinute = 1080
            },
            PlayerMessage = "帮我问一下艾莉娅，她记不记得花园钥匙放在哪里。",
            CorrelationId = $"demo-chat-{Guid.NewGuid():N}"
        };

        var beforeHook = new AgentChatTurnBeforeResult
        {
            ExtraContexts =
            [
                new AgentContextBlock(EnhanceMessageRule.WorldContext, "世界观：架空宅邸日常互动场景。"),
                new AgentContextBlock(EnhanceMessageRule.SceneState, "场景：傍晚，宅邸客厅，玩家正在寻找花园钥匙。"),
                new AgentContextBlock(EnhanceMessageRule.GoalContext, "目标：如果角色自己不确定，可以使用 ask_character 询问另一个角色。")
            ]
        };

        Console.WriteLine("=== Character Agent Demo ===");
        Console.WriteLine("chain: player -> 汐泠 CharacterAgent -> ask_character -> InterAgentMessenger -> 艾莉娅 CharacterAgent -> reply -> 汐泠 final response");
        Console.WriteLine($"character: {character.Name}");
        Console.WriteLine($"playerInput: {turnContext.PlayerMessage}");
        Console.WriteLine();

        DemonstrateMessageApi(turnContext.PlayerMessage);

        AgentMessage? reply;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var executionContext = new AgentExecutionContext(
                conversationId: turnContext.CorrelationId,
                startedAtUtc: now,
                overallDeadlineUtc: now.AddMinutes(5),
                stepIdleTimeout: TimeSpan.FromSeconds(60));

            reply = await runtimeHub.RequestAsync(
                characterId: "rin",
                type: AgentMessageType.Chat,
                from: "player",
                payload: turnContext.PlayerMessage,
                gameId: turnContext.GameId,
                correlationId: turnContext.CorrelationId,
                timeout: TimeSpan.FromMinutes(5),
                executionContext: executionContext,
                contextBlocks: beforeHook.ExtraContexts,
                blocks:
                [
                    new TextBlock
                    {
                        Kind = BlockKind.Text,
                        Text = turnContext.PlayerMessage
                    }
                ]);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine("character-agent demo failed during LLM request.");
            Console.Error.WriteLine($"reason: {ex.Message}");
            Console.Error.WriteLine("hint: check openLuo.playgraound/config/llm.demo.ini and any local proxy settings.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("character-agent demo failed.");
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        if (reply is null)
        {
            Console.Error.WriteLine("character-agent demo failed: no reply from runtime.");
            return 1;
        }

        var contextStore = provider.GetRequiredService<IAgentContextStore>();
        var ryuContext = await contextStore.GetOrCreateAsync("playground-agent-demo", "rin");
        var aliyaContext = await contextStore.GetOrCreateAsync("playground-agent-demo", "aliya");

        Console.WriteLine("=== Final Reply ===");
        Console.WriteLine(reply.Payload);
        Console.WriteLine();

        Console.WriteLine("=== Trace ===");
        if (reply.TraceLines is { Count: > 0 })
        {
            foreach (var line in reply.TraceLines)
                Console.WriteLine($"- {line}");
        }
        else
        {
            Console.WriteLine("<none>");
        }
        Console.WriteLine();

        Console.WriteLine("=== Visible Blocks ===");
        if (reply.VisibleBlocks is { Count: > 0 })
        {
            foreach (var block in reply.VisibleBlocks)
                Console.WriteLine(block);
        }
        else
        {
            Console.WriteLine("<none>");
        }
        Console.WriteLine();

        Console.WriteLine("=== Reply Blocks (structured) ===");
        if (reply.Blocks is { Count: > 0 })
        {
            foreach (var block in reply.Blocks)
                PrintBlock(block);
        }
        else
        {
            Console.WriteLine("<none> (use --payload fallback for plain text)");
        }
        Console.WriteLine();

        PrintConversation("汐泠 Runtime Conversation", ryuContext.Conversation);
        PrintConversation("Aliya Runtime Conversation", aliyaContext.Conversation);

        return 0;
    }

    private static ServiceCollection BuildServices(ILlmClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGameLogger, ConsoleGameLogger>();
        services.AddSingleton(client);
        services.AddSingleton<IOutputEventBus, InMemoryOutputEventBus>();
        services.AddSingleton<ITurnMessageEmitterFactory, SessionTurnMessageEmitterFactory>();
        services.AddSingleton<IRuntimeConfigCenter>(_ => new StaticRuntimeConfigCenter(new AppConfig
        {
            InterAgent = new InterAgentConfig
            {
                AskTimeoutSeconds = 30,
                SessionTurnTimeoutSeconds = 30,
                HiddenDialogueFuseTurns = 12
            }
        }));
        services.AddSingleton<IStructuredOutputParser, StructuredOutputParser>();

        services.AddSingleton<IExecutor<PlanInput, PlanOutput>, PlanExecutor>();
        services.AddSingleton<IExecutor<FlowRoutingInput, FlowRoutingOutput>, FlowRoutingExecutor>();
        services.AddSingleton<IExecutor<ToolUseInput, ToolUseOutput>, ToolUseExecutor>();
        services.AddSingleton<IExecutor<PlannedExecutionPlanInput, PlannedExecutionPlanOutput>, PlannedExecutionPlanExecutor>();
        services.AddSingleton<IExecutor<CharacterResponseInput, string>, CharacterResponseExecutor>();
        services.AddSingleton<IExecutor<StateUpdateInput, StateUpdateOutput>, StateUpdateExecutor>();
        services.AddSingleton<IExecutor<GiftIntentInput, GiftIntentOutput>, GiftIntentExecutor>();
        services.AddSingleton<IExecutorPromptBuilder<PlanInput>, PlanPromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<FlowRoutingInput>, FlowRoutingPromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<ToolUseInput>, ToolUsePromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<PlannedExecutionPlanInput>, PlannedExecutionPlanPromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<CharacterResponseInput>, CharacterResponsePromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<StateUpdateInput>, StateUpdatePromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<GiftIntentInput>, GiftIntentPromptBuilder>();

        services.AddSingleton<IAgentContextStore, InMemoryAgentContextStore>();
        services.AddSingleton<IAgentMemoryStore, DemoAgentMemoryStore>();
        services.AddSingleton<ICharacterMemoryGateway, CharacterMemoryGateway>();
        services.AddSingleton<ICharacterStateGateway, CharacterStateGateway>();
        services.AddSingleton<ICharacterPromptContextBuilder, CharacterPromptContextBuilder>();
        services.AddSingleton<IAgentProfileCatalog, DemoAgentProfileCatalog>();
        services.AddSingleton<ICosplaySkillProvider, DemoCosplaySkillProvider>();
        services.AddSingleton<IAgentRoster, DemoAgentRoster>();
        services.AddSingleton<IAgentDispatcher, AgentDispatcher>();
        services.AddSingleton<IAgentCapabilityRegistry, DemoCapabilityRegistry>();
        services.AddSingleton<ICharacterCapabilitySnapshotProvider, DefaultCharacterCapabilitySnapshotProvider>();
        services.AddSingleton<IAgentStepHook, NoOpAgentStepHook>();
        services.AddSingleton<IInterAgentMessenger, InterAgentMessenger>();
        services.AddSingleton<IAgentCapabilityExecutor, DemoCapabilityExecutor>();
        services.AddSingleton<ICharacterToolGateway, CharacterToolGateway>();

        services.AddSingleton<CharacterMemoryRecallNode>();
        services.AddSingleton<CharacterPlanNode>();
        services.AddSingleton<CharacterToolUseNode>();
        services.AddSingleton<CharacterResponseNode>();
        services.AddSingleton<CharacterStateUpdateNode>();
        services.AddSingleton<ICharacterExecutionPlanBuilder, DefaultCharacterExecutionPlanBuilder>();
        services.AddSingleton<CharacterPlannedExecutionNode>();

        services.AddSingleton<IAgentFlowRegistry, DefaultAgentFlowRegistry>();
        services.AddSingleton<IAgentFlowGuardEvaluator, DefaultAgentFlowGuardEvaluator>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterMemoryRecallFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterPlanFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterPlannedExecutionFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterToolUseFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterResponseFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterFinalizeReplyFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterStateUpdateFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowRunner, DefaultAgentFlowRunner>();
        services.AddSingleton<ICharacterAgent, CharacterAgent>();

        services.AddSingleton<ICharacterTurnRequestBuilder, DefaultCharacterTurnRequestBuilder>();
        services.AddSingleton<ICharacterTurnResultApplier, DefaultCharacterTurnResultApplier>();
        services.AddSingleton<IAgentMessageHandler, DefaultAgentMessageHandler>();
        services.AddSingleton<IAgentRuntimeHub, AgentRuntimeHub>();

        return services;
    }

    private static async Task SeedContextsAsync(IServiceProvider provider)
    {
        var store = provider.GetRequiredService<IAgentContextStore>();

        var ryu = await store.GetOrCreateAsync("playground-agent-demo", "rin");
        ryu.Summary =
            """
            affection=68
            trust=74
            mood=calm
            """;
        ryu.Conversation.Clear();
        ryu.Conversation.Add(new AgentConversationTurn
        {
            SpeakerId = "player",
            SpeakerRole = "inbound",
            Content = "你还记得我们上次把花园钥匙放到哪里了吗？"
        });
        ryu.Conversation.Add(new AgentConversationTurn
        {
            SpeakerId = "rin",
            SpeakerRole = "outbound",
            Content = "我只记得您那时说过，或许艾莉娅会更清楚。"
        });
        await store.SaveAsync(ryu);

        var aliya = await store.GetOrCreateAsync("playground-agent-demo", "aliya");
        aliya.Summary =
            """
            affection=50
            trust=80
            mood=calm
            """;
        aliya.Conversation.Clear();
        await store.SaveAsync(aliya);
    }

    private static void PrintConversation(string title, IReadOnlyList<AgentConversationTurn> turns)
    {
        Console.WriteLine($"=== {title} ===");
        if (turns.Count == 0)
        {
            Console.WriteLine("<empty>");
            Console.WriteLine();
            return;
        }

        foreach (var turn in turns)
            Console.WriteLine($"- {turn.SpeakerId}/{turn.SpeakerRole}: {turn.Content}");
        Console.WriteLine();
    }

    private static void DemonstrateMessageApi(string sampleText)
    {
        Console.WriteLine("--- Message / Block API Demo ---");

        var msg = Message.FromText(sampleText, speakerRole: "player", speakerId: "cli");
        Console.WriteLine($"Message.FromText  ->  MessageId={msg.MessageId}, SpeakerRole={msg.SpeakerRole}");
        Console.WriteLine($"  Blocks count: {msg.Blocks.Count}");
        foreach (var block in msg.Blocks)
            PrintBlock(block);

        var roundtripped = msg.ToPlainText();
        Console.WriteLine($"  ToPlainText roundtrip: \"{roundtripped}\"");
        Console.WriteLine($"  Match: {roundtripped == sampleText}");
        Console.WriteLine();

        var multiBlock = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SpeakerRole = "assistant",
            Blocks =
            [
                new TextBlock { Kind = BlockKind.Text, Text = "我帮你查了一下。" },
                new ImageBlock
                {
                    Kind = BlockKind.Image,
                    AssetId = "demo-image-001",
                    MimeType = "image/png",
                    Name = "花园钥匙位置图",
                    Caption = "钥匙在玄关第二层抽屉"
                }
            ]
        };
        Console.WriteLine($"Multi-block message: {multiBlock.Blocks.Count} blocks");
        foreach (var block in multiBlock.Blocks)
            PrintBlock(block);
        Console.WriteLine($"  ToPlainText: \"{multiBlock.ToPlainText()}\"");
        Console.WriteLine();
    }

    private static void PrintBlock(Block block)
    {
        switch (block)
        {
            case TextBlock text:
                Console.WriteLine($"  [Text] visibility={text.Visibility} text=\"{text.Text.Trim()}\"");
                break;
            case ImageBlock image:
                Console.WriteLine($"  [Image] assetId={image.AssetId} mime={image.MimeType} name={image.Name ?? "-"} caption={image.Caption ?? "-"}");
                break;
            case AssetBlock asset:
                Console.WriteLine($"  [Asset] assetId={asset.AssetId} mime={asset.MimeType} role={asset.BlobRole} name={asset.Name ?? "-"}");
                break;
            default:
                Console.WriteLine($"  [{block.Kind}] <unknown block type>");
                break;
        }
    }

    private static class DemoCharacters
    {
        public static readonly Character[] All =
        [
            new()
            {
                Id = "rin",
                GameId = "playground-agent-demo",
                ArchetypeId = "dragon-maid",
                Name = "汐泠"
            },
            new()
            {
                Id = "aliya",
                GameId = "playground-agent-demo",
                ArchetypeId = "maid",
                Name = "艾莉娅"
            }
        ];
    }

    private sealed class DemoAgentMemoryStore : IAgentMemoryStore
    {
        public Task StoreAgentEventAsync(string gameId, string characterId, string content, int emotionalWeight, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MemoryRecord>> RecallSharedAsync(string query, string gameId, string? excludeCharacterId, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>(
            [
                new MemoryRecord
                {
                    Id = "m-shared-1",
                    GameId = gameId,
                    OwnerCharacterId = "shared",
                    Scope = MemoryScope.Shared,
                    Summary = "上次整理花园工具时，艾莉娅提过钥匙后来被收进了玄关抽屉。",
                    RecallText = "花园钥匙在玄关抽屉",
                    SourceText = "艾莉娅提到花园钥匙放在玄关抽屉。"
                }
            ]);

        public Task<IReadOnlyList<MemoryRecord>> GetRecentPrivateAsync(string gameId, string characterId, int count, CancellationToken ct = default)
        {
            var summary = string.Equals(characterId, "aliya", StringComparison.OrdinalIgnoreCase)
                ? "你记得自己上次确实把花园钥匙收进了玄关第二层抽屉。"
                : "你记得自己并不确定钥匙位置，但记得艾莉娅更熟悉杂物整理。";

            return Task.FromResult<IReadOnlyList<MemoryRecord>>(
            [
                new MemoryRecord
                {
                    Id = $"m-private-{characterId}",
                    GameId = gameId,
                    OwnerCharacterId = characterId,
                    Scope = MemoryScope.CharacterPrivate,
                    Summary = summary,
                    RecallText = summary,
                    SourceText = summary
                }
            ]);
        }
    }

    private sealed class DemoCapabilityRegistry : IAgentCapabilityRegistry
    {
        public Task<AgentCapabilitySnapshot> BuildSnapshotAsync(AgentCapabilityContext context, CancellationToken ct = default) =>
            Task.FromResult(new AgentCapabilitySnapshot
            {
                Capabilities =
                [
                    new AgentCapabilityDescriptor
                    {
                        Name = "ask_character",
                        Category = "inter-agent",
                        Usage = "ask_character --target <角色> --question <问题>",
                        HelpShort = "询问另一个角色的意见",
                        ExecutorKind = "inter-agent",
                        RiskLevel = "low"
                    }
                ],
                KnownCharacters =
                [
                    new AgentKnownCharacter
                    {
                        CharacterId = "aliya",
                        DisplayName = "艾莉娅",
                        ArchetypeId = "maid"
                    }
                ]
            });
    }

    private sealed class DemoCapabilityExecutor : IAgentCapabilityExecutor
    {
        private readonly IInterAgentMessenger _messenger;

        public DemoCapabilityExecutor(IInterAgentMessenger messenger)
        {
            _messenger = messenger;
        }

        public async Task<CommandResult> ExecuteAsync(
            AgentCapabilityDescriptor capability,
            string[] args,
            Dictionary<string, string> options,
            AgentCapabilityContext context,
            CancellationToken ct = default)
        {
            if (!string.Equals(capability.Name, "ask_character", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Fail($"unsupported capability: {capability.Name}");

            var target = options.TryGetValue("target", out var targetValue) ? targetValue : "艾莉娅";
            var question = options.TryGetValue("question", out var questionValue) ? questionValue : string.Join(" ", args);
            var result = await _messenger.AskAsync(new InterAgentAskRequest
            {
                GameId = context.GameId,
                FromCharacterId = context.CharacterId,
                TargetSelector = target,
                Question = question,
                ExecutionContext = context.ExecutionContext
            }, ct);

            if (!result.Success)
                return CommandResult.Fail(result.Error);

            var commandResult = CommandResult.Ok($"来自 {result.TargetDisplayName} 的回复：\n{result.Reply}");
            if (result.Outcome is not null)
                commandResult.Metadata[CommandResultMetadataKeys.InterAgentOutcome] = result.Outcome;
            return commandResult;
        }
    }

    private sealed class DemoAgentProfileCatalog : IAgentProfileCatalog
    {
        public AgentProfile GetProfile(string characterId)
        {
            return characterId.ToLowerInvariant() switch
            {
                "rin" => new AgentProfile
                {
                    CharacterId = "rin",
                    DisplayName = "汐泠",
                    ArchetypeId = "dragon-maid",
                    RolePrompt =
                        """
                        名字：汐泠
                        身份：龙娘侍从
                        性格：沉静、忠诚、克制，会以照料者口吻回应主人
                        说话风格：简洁、温柔、可靠
                        """
                },
                "aliya" => new AgentProfile
                {
                    CharacterId = "aliya",
                    DisplayName = "艾莉娅",
                    ArchetypeId = "maid",
                    RolePrompt =
                        """
                        名字：艾莉娅
                        身份：宅邸女仆
                        性格：细心、温和、记性很好，擅长收纳整理
                        说话风格：礼貌、简洁、可靠
                        """
                },
                _ => new AgentProfile { CharacterId = characterId, DisplayName = characterId, RolePrompt = $"角色：{characterId}" }
            };
        }
    }

    private sealed class DemoCosplaySkillProvider : ICosplaySkillProvider
    {
        public IReadOnlyList<SkillDocument> GetPreloadedSkills(AgentProfile profile) => [];
    }

    private sealed class DemoAgentRoster : IAgentRoster
    {
        public Task<IReadOnlyList<Character>> ListAsync(string gameId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Character>>(DemoCharacters.All.Where(x => x.GameId == gameId).ToList());

        public Task<Character?> ResolveAsync(string gameId, string selector, CancellationToken ct = default)
        {
            var normalized = selector.Trim();
            var character = DemoCharacters.All.FirstOrDefault(x =>
                string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(x.Id, normalized, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult(character);
        }

        public Task<Character?> GetActiveAsync(GameState state, CancellationToken ct = default) =>
            ResolveAsync(state.Id, state.ActiveCharacterId, ct);

        public Task<Character?> SetActiveAsync(string gameId, string selector, CancellationToken ct = default) =>
            ResolveAsync(gameId, selector, ct);
    }
}
