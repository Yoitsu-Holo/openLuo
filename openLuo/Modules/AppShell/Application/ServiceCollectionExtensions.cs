using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Infrastructure;
using openLuo.Capabilities.Llm;
using openLuo.Composition;
using openLuo.Infrastructure.Conversation;
using openLuo.Infrastructure.Database;
using openLuo.Infrastructure.Logging;
using openLuo.Infrastructure.Resilience;
using openLuo.Infrastructure.Security;
using openLuo.Modules.Memory.Application;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Infrastructure.Retrieval;
using openLuo.Modules.Memory.Infrastructure.Storage;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Application.Runtime;
using openLuo.Modules.Agent.Infrastructure;
using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Embedding.Infrastructure;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Infrastructure.Chat;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Infrastructure.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using openLuo.Capabilities.Core.Models;

using openLuo.Core.Interfaces;

using openLuo.Infrastructure.IO;


namespace openLuo.Modules.AppShell.Application;

/// <summary>
/// 组合根（新架构白名单版）：仅注册新链路（IAgentRuntime 内核）消费的服务。
/// 旧业务链（SessionRuntime/Gameplay/Commanding/PluginRuntime/Assets 等）已物理删除。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenLuo(
        this IServiceCollection services, AppConfig config, string baseDir)
    {
        var rawPath = string.IsNullOrEmpty(config.DatabasePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openLuo", "game.db")
            : config.DatabasePath;

        var dbPath = rawPath.StartsWith("~/")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), rawPath[2..])
            : rawPath;

        var connectionString = $"Data Source={dbPath}";
        services.AddLogging();

        // ── 基础设施 ────────────────────────────────────────────────────
        services.AddSingleton(sp =>
            new DatabaseInitializer(
                connectionString,
                baseDir,
                config.SqliteVec.ExtensionPath,
                config.SqliteVec.VectorDimensions));

        // ── 世界状态（world 扩展能力） ──────────────────────────────────
        services.AddSingleton<StateStore>(sp => new StateStore(connectionString));
        services.AddSingleton<IStateStore>(sp => sp.GetRequiredService<StateStore>());
        services.AddSingleton<StateDefStore>(sp => new StateDefStore(connectionString));
        services.AddSingleton<IStateRegistry>(sp => new StateRegistry(sp.GetRequiredService<StateDefStore>()));
        services.AddSingleton<IStateMutationService>(sp => new StateMutationService(
            sp.GetRequiredService<IStateRegistry>(),
            sp.GetRequiredService<IStateStore>()));
        services.AddSingleton<IStateQueryService>(sp => new StateQueryService(
            sp.GetRequiredService<IStateRegistry>(),
            sp.GetRequiredService<IStateStore>()));
        services.AddSingleton<IStateSnapshotBuilder>(sp => new StateSnapshotBuilder(
            sp.GetRequiredService<IStateQueryService>()));

        // ── Agent 端口（party 扩展） ────────────────────────────────────
        services.AddSingleton<IAgentRoster>(sp => new ArchetypeAgentRoster(baseDir));
        services.AddSingleton<IAgentRuntimeHub>(sp => new AgentRuntimeHub(() => sp.GetRequiredService<IAgentRuntime>()));

        // ── LLM / Embedding ─────────────────────────────────────────────
        services.AddSingleton<ILlmClient>(_ => new RuntimeConfiguredLlmClient(() => config.Llm));
        services.AddSingleton<IEmbeddingClient>(_ => new RuntimeConfiguredEmbeddingClient(() => config.Embedding));

        // ── Memory（memory 扩展能力） ───────────────────────────────────
        services.AddSingleton<IDatabaseConnectionFactory>(_ =>
            new SqliteConnectionFactory(connectionString, baseDir, config.SqliteVec.ExtensionPath));
        services.AddSingleton<IMemoryRepository, SqliteMemoryRepository>();
        services.AddSingleton<IMemoryWriteProjector, DefaultMemoryWriteProjector>();
        services.AddSingleton<VectorMemoryRetriever>();
        services.AddSingleton<KeywordMemoryRetriever>();
        services.AddSingleton<IMemoryRetriever, CompositeMemoryRetriever>();
        services.AddSingleton<IMemoryRecallService, MemoryRecallCoordinator>();
        services.AddSingleton<IMemoryWriteService, MemoryCommitCoordinator>();

        // ── 日志 ────────────────────────────────────────────────────────
        services.AddSingleton<IGameStreams, ConsoleStreams>();
        var logDir = Path.Combine(baseDir, "logs");
        services.AddSingleton<GameLogger>(sp => new GameLogger(logDir, config: null, sp.GetRequiredService<IGameStreams>()));
        services.AddSingleton<IGameLogger>(sp => sp.GetRequiredService<GameLogger>());

        // ── 内核组合根（新架构） ────────────────────────────────────────
        services.AddSingleton<SessionStore>();
        services.AddSingleton(sp => new ExtensionRegistry(sp));
        services.AddSingleton<ExtensionCapabilitySource>();
        services.AddSingleton<ICapabilitySource>(sp => sp.GetRequiredService<ExtensionCapabilitySource>());
        services.AddSingleton<IContextContributor, TimeContextContributor>();
        services.AddSingleton<IContextContributor, CapabilityContextContributor>();
        services.AddSingleton<IContextContributor, PlatformContextContributor>();
        services.AddSingleton<HttpClient>();

        services.AddSingleton<IConversationStore>(sp => new SqliteConversationStore(connectionString));
        services.AddSingleton<IOutputQueue, InMemoryOutputQueue>();
        services.AddSingleton<IMessageTagPipeline, DefaultMessageTagPipeline>();
        services.AddSingleton<IContextAssembler>(sp =>
        {
            var registry = sp.GetRequiredService<ExtensionRegistry>();
            var contributors = sp.GetServices<IContextContributor>().Concat(registry.Contributors);
            return new DefaultContextAssembler(contributors);
        });
        services.AddSingleton<IContextUpdater, SessionContextUpdater>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICapabilityDecisionModel, LlmCapabilityDecisionModel>();
        services.AddSingleton<ICapabilityPolicy, DefaultCapabilityPolicy>();
        services.AddSingleton<IStateTransaction, InMemoryStateTransaction>();
        services.AddSingleton<ICapabilityInvoker, UnboundCapabilityInvoker>();
        services.AddSingleton<ICapabilityDispatcher>(sp =>
        {
            var registry = sp.GetRequiredService<ExtensionRegistry>();
            var invokers = new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in registry.Invokers)
                invokers[pair.Key] = pair.Value;

            foreach (var source in sp.GetServices<ICapabilitySource>())
            {
                var descriptorInvoker = source switch
                {
                    openLuo.Capabilities.Mcp.McpCapabilitySource mcp when mcp.IsHealthy => (ICapabilityInvoker?)mcp.CreateInvoker(),
                    openLuo.Capabilities.A2A.A2ACapabilitySource a2a when a2a.IsHealthy => a2a.CreateInvoker(),
                    _ => null
                };
                if (descriptorInvoker is null)
                    continue;
                foreach (var descriptor in source.ListCapabilities())
                    invokers[descriptor.CanonicalId] = descriptorInvoker;
            }
            return new DefaultCapabilityDispatcher(
                sp.GetRequiredService<ICapabilityInvoker>(), sp.GetRequiredService<ICapabilityPolicy>(),
                sp.GetRequiredService<IStateTransaction>(), canonicalInvokers: invokers,
                logger: sp.GetService<Core.Interfaces.IGameLogger>());
        });
        services.AddSingleton<ICapabilityDecisionLoop, DefaultCapabilityDecisionLoop>();
        services.AddSingleton<ICapabilityCatalog>(sp =>
        {
            var catalog = new DefaultCapabilityCatalog(sp.GetServices<ICapabilitySource>());
            catalog.LoadBase();
            return catalog;
        });
        services.AddSingleton<IAgentRuntime>(sp => new ComposedAgentRuntime(
            sp.GetRequiredService<ICapabilityCatalog>(),
            sp.GetRequiredService<ICapabilityDecisionLoop>(),
            sp.GetRequiredService<IContextAssembler>(),
            sp.GetRequiredService<IConversationStore>(),
            sp.GetRequiredService<IMessageTagPipeline>(),
            sp.GetRequiredService<IOutputQueue>(),
            sp.GetRequiredService<SessionStore>(),
            sp.GetService<DatabaseInitializer>()));
        return services;
    }
}
