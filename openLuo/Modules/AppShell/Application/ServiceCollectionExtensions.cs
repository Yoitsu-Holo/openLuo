using openLuo.Modules.Content.Application.Loaders;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Application.Validation;
using openLuo.Modules.Content.Application;
using openLuo.Modules.AgentCapabilities.Application;
using openLuo.Modules.AgentCapabilities.Core.Interfaces;
using openLuo.Modules.InterAgent.Application;
using openLuo.Modules.InterAgent.Core.Interfaces;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Infrastructure;
using openLuo.Modules.Gameplay.Application.Services;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Infrastructure;
using openLuo.Modules.GameBridge.Infrastructure;
using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Infrastructure;
using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Embedding.Infrastructure;
using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Application.GiftIntent;
using openLuo.Modules.Executor.Application.GoalExecution;
using openLuo.Modules.Executor.Application.RandomImage;
using openLuo.Modules.Executor.Application.StateUpdate;
using openLuo.Modules.Executor.Application.TODOList;
using openLuo.Modules.Executor.Application.ToolUse;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Application;
using openLuo.Modules.Memory.Infrastructure.Retrieval;
using openLuo.Modules.Memory.Infrastructure.Storage;
using openLuo.Modules.WorldState.Application.Services.TimeProviders;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.Gameplay.Core.Interfaces;
using openLuo.Modules.WorldState.Infrastructure.Resources;
using openLuo.Modules.WorldState.Infrastructure.State;
using openLuo.Modules.WorldState.Infrastructure.Timeline;
using openLuo.Core;
using openLuo.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Infrastructure.Database;
using openLuo.Infrastructure.IO;
using openLuo.Modules.Llm.Infrastructure.Chat;
using openLuo.Infrastructure.Logging;
using openLuo.Interfaces.QQbot;
using openLuo.Modules.SessionRuntime.Application;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using WorldTimeService = openLuo.Modules.WorldState.Application.Services.TimeService;
using GameStateApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.GameStateApiHandler;
using LifecycleApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.LifecycleApiHandler;
using PlayerApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.PlayerApiHandler;
using ShopApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.ShopApiHandler;
using GiftApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.GiftApiHandler;
using HostBridgeApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.HostBridgeApiHandler;
using StateApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.StateApiHandler;
using ResourceApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.ResourceApiHandler;
using AssetApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.AssetApiHandler;
using TimelineApiHandler = openLuo.Modules.GameBridge.Infrastructure.Handlers.TimelineApiHandler;

namespace openLuo.Modules.AppShell.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenLuo(
        this IServiceCollection services, AppConfig config, string baseDir) =>
        services.AddOpenLuo(new StaticRuntimeConfigCenter(config), baseDir);

    public static IServiceCollection AddOpenLuo(
        this IServiceCollection services, IRuntimeConfigCenter configCenter, string baseDir)
    {
        var config = configCenter.GetSnapshot();
        var rawPath = string.IsNullOrEmpty(config.DatabasePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openLuo", "game.db")
            : config.DatabasePath;

        var dbPath = rawPath.StartsWith("~/")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), rawPath[2..])
            : rawPath;

        var connectionString = $"Data Source={dbPath}";
        services.AddLogging();
        services.AddSingleton<IRuntimeConfigCenter>(configCenter);

        // Infrastructure
        services.AddSingleton(sp =>
            new DatabaseInitializer(
                connectionString,
                baseDir,
                config.SqliteVec.ExtensionPath,
                config.SqliteVec.VectorDimensions));
        services.AddSingleton<IGameStateRepository>(sp => new GameStateRepository(
            connectionString,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameStateRepository>>(),
            sp.GetRequiredService<IGameContextAccessor>()));
        services.AddSingleton<ICharacterRepository>(sp => new CharacterRepository(connectionString, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CharacterRepository>>()));
        services.AddSingleton<IInventoryRepository>(sp => new InventoryRepository(connectionString, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InventoryRepository>>()));
        // IGameStreams 由调用方根据模式注册（CLI 或 TUI）

        // State system
        services.AddSingleton<StateStore>(
            sp => new StateStore(connectionString));
        services.AddSingleton<IStateStore>(
            sp => sp.GetRequiredService<StateStore>());
        services.AddSingleton<StateDefStore>(sp =>
            new StateDefStore(connectionString));
        services.AddSingleton<IStateRegistry>(sp =>
            new StateRegistry(
                sp.GetRequiredService<StateDefStore>()));
        services.AddSingleton<IStateMutationService>(sp =>
            new StateMutationService(
                sp.GetRequiredService<IStateRegistry>(),
                sp.GetRequiredService<IStateStore>()));
        services.AddSingleton<IStateQueryService>(sp =>
            new StateQueryService(
                sp.GetRequiredService<IStateRegistry>(),
                sp.GetRequiredService<IStateStore>()));
        services.AddSingleton<IStateSnapshotBuilder>(sp =>
            new StateSnapshotBuilder(
                sp.GetRequiredService<IStateQueryService>()));
        services.AddSingleton<IResourceCatalogService, ResourceCatalogService>();
        services.AddSingleton<IResourceValueService, ResourceValueService>();
        services.AddSingleton<IResourceStatusProjectionService, ResourceStatusProjectionService>();
        services.AddSingleton<IResourceEvaluationProjectionService, ResourceEvaluationProjectionService>();
        services.AddSingleton<IResourceLifecycleService, ResourceLifecycleService>();
        services.AddSingleton<ITimeProvider, VirtualTimeProvider>();
        services.AddSingleton<ITimeProvider, RealtimeTimeProvider>();
        services.AddSingleton<ITimeProvider, DisabledTimeProvider>();
        services.AddSingleton<ITimeService, WorldTimeService>();
        services.AddSingleton<IGameContextAccessor, AsyncLocalGameContextAccessor>();
        services.AddSingleton<ITimelineService>(sp => new TimelineService(connectionString));

        // Application services
        services.AddSingleton<IStateEvaluationCoordinator, StateEvaluationCoordinator>();
        services.AddSingleton<IStatusAggregator, StatusAggregator>();
        services.AddSingleton<ICommandGate, CommandGate>();
        services.AddSingleton<ICommandConfirmationService, CommandConfirmationService>();
        services.AddSingleton<openLuo.Infrastructure.Security.RateLimiter>();
        services.AddSingleton<openLuo.Infrastructure.Security.InputValidator>();
        services.AddSingleton<openLuo.Infrastructure.Resilience.RetryPolicies>();
        services.AddSingleton<IAgentContextStore>(sp => new SqliteAgentContextStore(
            connectionString,
            sp.GetRequiredService<IRuntimeConfigCenter>()));
        services.AddSingleton<IAgentProfileCatalog, CharacterArchetypeAgentProfileCatalog>();
        services.AddSingleton<ICosplaySkillProvider, CharacterArchetypeCosplaySkillProvider>();
        services.AddSingleton<IAgentRoster, RepositoryAgentRoster>();
        services.AddSingleton<IAgentCommandBridge, PluginAgentCommandBridge>();
        services.AddSingleton<IInterAgentMessenger, InterAgentMessenger>();
        services.AddSingleton<IAgentCapabilityRegistry, UnifiedAgentCapabilityRegistry>();
        services.AddSingleton<IAgentCapabilityExecutor, UnifiedAgentCapabilityExecutor>();
        services.AddSingleton<IAgentTaskStore, PartyTaskStoreAdapter>();
        services.AddSingleton<IAgentChatHookStage, QqBotReplyStyleChatHook>();
        services.AddSingleton<IAgentChatHookStage, PluginChatHookStage>();
        services.AddSingleton<IAgentChatHookStage, GiftIntentChatHook>();
        services.AddSingleton<IAgentChatHookStage, PostChatStateEvaluationHook>();
        services.AddSingleton<IAgentChatHook, CompositeAgentChatHook>();
        services.AddSingleton<IAgentStepHook, NoOpAgentStepHook>();
        services.AddSingleton<IStructuredOutputParser, StructuredOutputParser>();
        services.AddSingleton<IExecutor<FlowRoutingInput, FlowRoutingOutput>, FlowRoutingExecutor>();
        services.AddSingleton<IExecutor<GiftIntentInput, GiftIntentOutput>, GiftIntentExecutor>();
        services.AddSingleton<IExecutor<ToolUseInput, ToolUseOutput>, ToolUseExecutor>();
        services.AddSingleton<IExecutor<CharacterResponseInput, string>, CharacterResponseExecutor>();
        services.AddSingleton<IExecutor<StateUpdateInput, StateUpdateOutput>, StateUpdateExecutor>();
        services.AddSingleton<IExecutor<TODOListInput, TODOListOutput>, TODOListExecutor>();
        services.AddSingleton<IExecutor<GoalExecutorInput, GoalExecutorOutput>, GoalExecutor>();
        services.AddHttpClient<IExecutor<RandomImageFetchInput, RandomImageFetchOutput>, RandomImageFetchExecutor>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        });
        services.AddSingleton<IExecutorPromptBuilder<FlowRoutingInput>, FlowRoutingPromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<GiftIntentInput>, GiftIntentPromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<ToolUseInput>, ToolUsePromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<CharacterResponseInput>, CharacterResponsePromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<StateUpdateInput>, StateUpdatePromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<TODOListInput>, TODOListPromptBuilder>();
        services.AddSingleton<IExecutorPromptBuilder<GoalExecutorInput>, GoalExecutorPromptBuilder>();
        services.AddSingleton<ICharacterMemoryGateway, CharacterMemoryGateway>();
        services.AddSingleton<ICharacterStateGateway, CharacterStateGateway>();
        services.AddSingleton<ICharacterToolGateway, CharacterToolGateway>();
        services.AddSingleton<ICharacterPromptContextBuilder, CharacterPromptContextBuilder>();
        services.AddSingleton<CharacterMemoryRecallNode>();
        services.AddSingleton<CharacterResponseNode>();
        services.AddSingleton<CharacterStateUpdateNode>();
        services.AddSingleton<CharacterTODOListNode>();
        services.AddSingleton<CharacterExecNode>();
        services.AddSingleton<IAgentFlowRegistry, DefaultAgentFlowRegistry>();
        services.AddSingleton<IAgentFlowGuardEvaluator, DefaultAgentFlowGuardEvaluator>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterMemoryRecallFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterStateUpdateFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterTODOListFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor, CharacterExecFlowNodeExecutor>();
        services.AddSingleton<IAgentFlowNodeExecutor>(sp => new SubgraphFlowNodeExecutor(sp));
        services.AddSingleton<ITurnMessageEmitterFactory, SessionTurnMessageEmitterFactory>();
        services.AddSingleton<IAgentFlowRunner>(sp => new DefaultAgentFlowRunner(
            sp.GetRequiredService<IAgentFlowRegistry>(),
            sp.GetRequiredService<IAgentFlowGuardEvaluator>(),
            sp.GetRequiredService<IEnumerable<IAgentFlowNodeExecutor>>(),
            sp.GetRequiredService<IExecutor<FlowRoutingInput, FlowRoutingOutput>>(),
            sp.GetRequiredService<IOutputEventBus>()));
        services.AddSingleton<ICharacterAgent, CharacterAgent>();
        services.AddSingleton<ICharacterTurnRequestBuilder, DefaultCharacterTurnRequestBuilder>();
        services.AddSingleton<ICharacterCapabilitySnapshotProvider, DefaultCharacterCapabilitySnapshotProvider>();
        services.AddSingleton<ICharacterTurnResultApplier, DefaultCharacterTurnResultApplier>();
        services.AddSingleton<IPlayerChatDispatcher, PlayerChatDispatcher>();
        services.AddSingleton<IAgentInvocationRouter, AgentInvocationRouter>();
        services.AddSingleton<IAgentDispatcher, AgentDispatcher>();
        services.AddSingleton<IAgentMessageHandler, DefaultAgentMessageHandler>();
        services.AddSingleton<IAgentRuntimeHub, AgentRuntimeHub>();
        services.AddSingleton<IMultiCharacterCommandCatalog, MultiCharacterCommandCatalog>();
        services.AddSingleton<IPartyTaskRepository, PartyTaskRepository>();
        services.AddSingleton<IMultiCharacterOrchestrator, MultiCharacterOrchestrator>();
        var logDir = Path.Combine(baseDir, "logs");
        services.AddSingleton<GameLogger>(sp => new GameLogger(logDir, sp.GetRequiredService<IRuntimeConfigCenter>(), sp.GetRequiredService<IGameStreams>()));
        services.AddSingleton<IGameLogger>(sp => sp.GetRequiredService<GameLogger>());

        services.AddSingleton<ILlmClient>(sp => new RuntimeConfiguredLlmClient(
            sp.GetRequiredService<IRuntimeConfigCenter>(),
            sp.GetRequiredService<IGameLogger>()));
        services.AddSingleton<IEmbeddingClient>(sp => new RuntimeConfiguredEmbeddingClient(
            sp.GetRequiredService<IRuntimeConfigCenter>(),
            sp.GetRequiredService<IGameLogger>()));
        services.AddSingleton<IDatabaseConnectionFactory>(_ =>
            new SqliteConnectionFactory(connectionString, baseDir, config.SqliteVec.ExtensionPath));
        services.AddSingleton<IMemoryRepository, SqliteMemoryRepository>();
        services.AddSingleton<IMemoryWriteProjector, DefaultMemoryWriteProjector>();
        services.AddSingleton<VectorMemoryRetriever>();
        services.AddSingleton<KeywordMemoryRetriever>();
        services.AddSingleton<IMemoryRetriever, CompositeMemoryRetriever>();
        services.AddSingleton<IMemoryRecallService, MemoryRecallCoordinator>();
        services.AddSingleton<IMemoryWriteService, MemoryCommitCoordinator>();
        services.AddSingleton<IAgentMemoryStore, AgentMemoryStoreAdapter>();

        // Content registry
        services.AddSingleton<IContentValidator, BasicContentValidator>();
        services.AddSingleton(sp =>
        {
            var validator = sp.GetRequiredService<IContentValidator>();
            var builder = new ContentRegistryBuilder(validator);

            var archetypes = CharacterArchetypeLoader.LoadAll(baseDir);
            var archetypeManifest = CharacterArchetypeLoader.LoadPack(archetypes);
            builder.AddPack(archetypeManifest);
            foreach (var archetype in archetypes)
                builder.AddDefinition(archetype, archetypeManifest.Id);

            foreach (var itemPack in ItemContentPackLoader.LoadAll(baseDir))
            {
                builder.AddPack(itemPack.Manifest);
                foreach (var item in itemPack.Items)
                    builder.AddDefinition(item, itemPack.Manifest.Id);
            }

            foreach (var pluginManifest in PluginContentLoader.LoadRuntimePluginManifests(baseDir))
                builder.AddPack(pluginManifest);

            foreach (var resource in PluginContentLoader.LoadResourceDefinitions(baseDir))
                builder.AddDefinition(resource, sourcePack: resource.Metadata.TryGetValue("namespace", out var ns) ? $"plugin.resource.{ns}" : "plugin.resources", isLegacy: true);

            foreach (var defaultConfig in PluginContentLoader.LoadDefaultConfigs(baseDir))
                builder.AddDefinition(defaultConfig, sourcePack: defaultConfig.PluginId, sourcePath: defaultConfig.Metadata.GetValueOrDefault("source_path"));

            foreach (var characterOverride in PluginContentLoader.LoadCharacterConfigOverrides(baseDir))
                builder.AddDefinition(characterOverride, sourcePack: characterOverride.PluginId, sourcePath: characterOverride.Metadata.GetValueOrDefault("source_path"));

            foreach (var tool in ToolDocLoader.LoadAll(baseDir))
                builder.AddDefinition(tool, sourcePack: "legacy.tools", isLegacy: true);

            foreach (var skill in SkillDocLoader.LoadAll(baseDir))
                builder.AddDefinition(skill, sourcePack: "legacy.skills", isLegacy: true);

            return builder.Build();
        });
        services.AddSingleton<CharacterArchetypeCatalog>();
        services.AddSingleton<ItemDefinitionCatalog>();
        services.AddSingleton(sp => new ShopOfferRepository(connectionString, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ShopOfferRepository>>()));
        services.AddSingleton<ShopService>();
        services.AddSingleton<GiftService>();

        services.AddSingleton<ISessionRegistry, InMemorySessionRegistry>();
        services.AddSingleton<IOutputEventBus, InMemoryOutputEventBus>();
        services.AddSingleton<IInputContentStore, InMemoryInputContentStore>();
        services.AddSingleton<IAttachmentAssetBridge, AttachmentAssetBridge>();
        services.AddSingleton<IInputRouter, DefaultInputRouter>();
        services.AddSingleton<ISessionExecutionContextAccessor, AsyncLocalSessionExecutionContextAccessor>();
        services.AddSingleton<ISessionBootstrapper, ContentRegistrySessionBootstrapper>();
        services.AddSingleton<IGameSessionRuntime>(sp => new GameSessionRuntime(
            sp.GetRequiredService<ISessionRegistry>(),
            sp.GetRequiredService<IOutputEventBus>(),
            sp.GetRequiredService<IInputContentStore>(),
            sp.GetRequiredService<IAttachmentAssetBridge>(),
            sp.GetRequiredService<IInputRouter>(),
            sp.GetRequiredService<ISessionExecutionContextAccessor>(),
            sp.GetRequiredService<IGameContextAccessor>(),
            sp.GetRequiredService<IAssetStore>(),
            sp.GetRequiredService<IAssetBlobStore>(),
            sp.GetRequiredService<IGameEngine>(),
            sp.GetRequiredService<IAgentRuntimeHub>(),
            sp.GetRequiredService<DatabaseInitializer>(),
            sp.GetRequiredService<IPluginHost>(),
            sp.GetRequiredService<ISessionBootstrapper>(),
            sp.GetRequiredService<ContentRegistry>(),
            sp.GetRequiredService<ICharacterRepository>(),
            sp.GetRequiredService<IStatusAggregator>(),
            sp.GetRequiredService<IGameStateRepository>(),
            sp.GetRequiredService<IAgentContextStore>(),
            baseDir));
        services.AddSingleton<ISessionGameApiFactory, SessionGameApiFactory>();
        services.AddSingleton<IGameSessionCatalog, GameSessionCatalog>();

        // Domain handlers
        services.AddSingleton<GameStateApiHandler>();
        services.AddSingleton<LifecycleApiHandler>();
        services.AddSingleton<PlayerApiHandler>();
        services.AddSingleton<ShopApiHandler>();
        services.AddSingleton<GiftApiHandler>();
        services.AddSingleton<HostBridgeApiHandler>();
        services.AddSingleton<StateApiHandler>();
        services.AddSingleton<ResourceApiHandler>();
        services.AddSingleton<TimelineApiHandler>();
        services.AddSingleton<IGameBridgeContextAccessor, AsyncLocalGameBridgeContextAccessor>();

        // Asset system
        services.AddSingleton<AssetDefStore>(sp =>
            new AssetDefStore(connectionString));
        services.AddSingleton<AssetStore>(
            sp => new AssetStore(connectionString));
        services.AddSingleton<IAssetStore>(
            sp => sp.GetRequiredService<AssetStore>());
        services.AddSingleton<IAssetRegistry>(sp =>
            new AssetRegistry(
                sp.GetRequiredService<AssetDefStore>()));
        services.AddSingleton<IAssetBlobStore>(
            sp => new AssetBlobStore(connectionString));
        services.AddSingleton<IAssetMetaStore>(
            sp => new AssetMetaStore(connectionString));
        services.AddSingleton<IAssetLinkService>(
            sp => new AssetLinkService(connectionString));
        services.AddSingleton<IAssetQueryService>(sp =>
            new AssetQueryService(
                sp.GetRequiredService<IAssetStore>(),
                sp.GetRequiredService<IAssetLinkService>()));
        services.AddSingleton<IAssetUnlockService>(
            sp => new AssetUnlockService(connectionString));
        services.AddSingleton<AssetApiHandler>();

        // GameApi dispatcher — attribute-driven route scanning
        services.AddSingleton<GameApiDispatcher>(sp => new GameApiDispatcher(
            typeof(GameStateApiHandler),
            typeof(PlayerApiHandler),
            typeof(ShopApiHandler),
            typeof(GiftApiHandler),
            typeof(HostBridgeApiHandler),
            typeof(LifecycleApiHandler),
            typeof(StateApiHandler),
            typeof(ResourceApiHandler),
            typeof(AssetApiHandler),
            typeof(TimelineApiHandler)));

        services.AddSingleton<GameApiHandler>();
        services.AddSingleton<IGameApiMediator>(sp => sp.GetRequiredService<GameApiHandler>());        services.AddSingleton<IPluginHost>(sp =>
        {
            var apiMediator = sp.GetRequiredService<IGameApiMediator>();
            var host = new McpPluginHost(
                baseDir,
                sp.GetRequiredService<IGameLogger>(),
                sp.GetRequiredService<IRuntimeConfigCenter>(),
                sp.GetRequiredService<IAgentFlowRegistry>(),
                sp.GetRequiredService<ISessionExecutionContextAccessor>(),
                sp.GetRequiredService<IGameBridgeContextAccessor>(),
                sp.GetRequiredService<ITimeService>(),
                sp.GetRequiredService<IResourceStatusProjectionService>());
            if (apiMediator is GameApiHandler gameApiHandler)
                gameApiHandler.SetPluginHost(host);
            host.SetApiHandler(apiMediator);
            return host;
        });

        // Engine (no built-in command handlers — all commands are plugin-based)
        services.AddSingleton<IGameEngine>(sp => new GameEngine(
            sp.GetRequiredService<IGameStateRepository>(),
            sp.GetRequiredService<ICharacterRepository>(),
            sp.GetRequiredService<ISessionBootstrapper>(),
            sp.GetRequiredService<IAgentInvocationRouter>(),
            sp.GetRequiredService<IGameLogger>(),
            sp.GetRequiredService<ICommandGate>()));

        return services;
    }
}
