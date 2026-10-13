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
    /// <summary>单对话同时可加载的技能数上限（按对话计；超出淘汰最久未用）——控制"已加载正文"占用的预算。</summary>
    private const int LoadedSkillCapacity = 3;

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
        services.AddSingleton(sp => new LogStore(logDir, config.Log?.Hot, config.Log?.Archive));
        // ⚠ 同一实例被两个描述符（LogStore / ILogStore）引用 → 容器释放时会**对它调用两次 DisposeAsync**。
        // 因此任何这样暴露的可释放类型都必须幂等（LogStore.DisposeAsync 已幂等；详见其注释与 LogStoreLifetimeTests）。
        services.AddSingleton<ILogStore>(sp => sp.GetRequiredService<LogStore>());
        services.AddSingleton<GameLogger>(sp => new GameLogger(logDir, config: null, sp.GetRequiredService<IGameStreams>(), sp.GetRequiredService<LogStore>()));
        services.AddSingleton<IGameLogger>(sp => sp.GetRequiredService<GameLogger>());

        // ── 资产（二进制经引用传递，见协议 §9） ─────────────────────────
        services.AddSingleton<openLuo.Core.Interfaces.IAssetStore>(sp =>
            new openLuo.Infrastructure.Assets.FileAssetStore(Path.Combine(baseDir, "assets")));

        // ── Hub 控制面状态持久化（hub.db；决策 #3） ──────────────────────
        services.AddSingleton(sp => new openLuo.Infrastructure.Persistence.HubStore(Path.Combine(baseDir, "hub.db")));

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
        // 上下文预算（D44）：token 计数 + 限额来源。限额经端口取当前快照 → 配置热加载对
        // 长生命周期的装配器/会话立即生效（与 HubServerOptions.Security 的做法一致）。
        services.AddSingleton<ITokenCounter>(_ => HeuristicTokenCounter.Instance);
        services.AddSingleton<IContextLimitsProvider, ConfigContextLimitsProvider>();
        services.AddSingleton<IContextAssembler>(sp =>
        {
            var registry = sp.GetRequiredService<ExtensionRegistry>();
            var contributors = sp.GetServices<IContextContributor>().Concat(registry.Contributors);
            return new DefaultContextAssembler(
                contributors,
                sp.GetRequiredService<ITokenCounter>(),
                sp.GetRequiredService<IContextLimitsProvider>());
        });
        // ⚠ 装配器同时挂在 IContextAssembler 与 IContextGlobalCache 两个描述符下（同一实例）：
        //    容器释放会对它调用两次 Dispose——它不可释放，安全（参见 LogStore 的教训）。
        services.AddSingleton<IContextGlobalCache>(sp => (IContextGlobalCache)sp.GetRequiredService<IContextAssembler>());
        services.AddSingleton<IContextUpdater, SessionContextUpdater>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICapabilityDecisionModel, LlmCapabilityDecisionModel>();
        services.AddSingleton<ICapabilityPolicy, DefaultCapabilityPolicy>();
        // 状态事务 + 持久化：状态快照落 hub.db（subject_state 表），重启后**版本与值保留**。
        // ⚠ 同一实例挂两个描述符（IStatePersistence / IStateReader 与 IStateTransaction）时，
        //    容器释放会对它调用两次 DisposeAsync——该类型不可释放，安全（参见 LogStore 的教训）。
        services.AddSingleton<IStatePersistence>(sp => sp.GetRequiredService<openLuo.Infrastructure.Persistence.HubStore>());
        services.AddSingleton<IStateTransaction>(sp => new InMemoryStateTransaction(sp.GetService<IStatePersistence>()));
        // 状态只读端口（Hub 投影 /v1/sessions/{id}/state 与 state.updated）指向同一实例。
        // ⚠ 同一实例挂两个描述符 → 容器释放时会对它调用两次 DisposeAsync，故实现必须幂等；
        //    InMemoryStateTransaction 不可释放，安全（参见 LogStore 的教训）。
        services.AddSingleton<IStateReader>(sp => (IStateReader)sp.GetRequiredService<IStateTransaction>());
        services.AddSingleton<ICapabilityInvoker, UnboundCapabilityInvoker>();
        services.AddSingleton<openLuo.Server.HubConfirmationGate>();
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
                logger: sp.GetService<Core.Interfaces.IGameLogger>(),
                confirmationGate: sp.GetRequiredService<openLuo.Server.HubConfirmationGate>());
        });
        services.AddSingleton<ICapabilityDecisionLoop, DefaultCapabilityDecisionLoop>();

        // ── 技能 / 工作流（D12/D13/D30）────────────────────────────────
        // 机制早就在（SkillDocument/ISkillService、WorkflowDefinition/IWorkflowRunner、扩展的 AddSkill/AddWorkflow），
        // 但从未接线：目录恒空、正文加载与编排执行都没有入口。这里把它接起来：
        //   技能 → ISkillService（摘要进目录、正文按需 core:load_skill）
        //   工作流 → DefaultWorkflowRunner（按 HandlerId 分发节点；core:run_workflow 黑盒触发）
        // ⚠ 全部在解析时从注册表按需读取：扩展是启动后加载的，早解析会拿到空表。
        // 技能有两个来源：扩展用 AddSkill 声明的（代码侧）+ 作者写在 data/skills/*.md 的（文件侧）。
        // **只在启动期加载一次**（重启即更新）——刻意不做热加载：技能正文的版本与进程生命周期绑定，
        // 避免"同一段对话里出现半旧半新的正文"这类需要持续维护的一致性问题（见 skills-workflows.md 决策记录）。
        services.AddSingleton<ISkillService>(sp => new DefaultSkillService(
            [
                .. sp.GetRequiredService<ExtensionRegistry>().Skills,
                .. openLuo.Infrastructure.Skills.SkillFileLoader.Load(Path.Combine(baseDir, "data", "skills")),
            ]));
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<ExtensionRegistry>();
            var runner = new DefaultWorkflowRunner(registry.WorkflowHandlers);
            foreach (var workflow in registry.Workflows)
                runner.Register(workflow);
            return runner;
        });
        services.AddSingleton<IWorkflowRunner>(sp => sp.GetRequiredService<DefaultWorkflowRunner>());
        // 已加载技能的**引用**集合（按对话分区，上限 3）＋把它展开成正文的贡献者：
        // core:load_skill 只登记 id，正文由贡献者在每个请求里从技能库取最新内容拼进系统块。
        services.AddSingleton<ILoadedSkillSet>(_ => new InMemoryLoadedSkillSet(capacity: LoadedSkillCapacity));
        services.AddSingleton<IContextContributor, LoadedSkillsContributor>();
        services.AddSingleton<ICapabilitySource>(sp =>
            new SkillCapabilitySource(sp.GetRequiredService<ISkillService>(), sp.GetRequiredService<ILoadedSkillSet>()));
        services.AddSingleton<ICapabilitySource>(sp =>
            new UnloadSkillCapabilitySource(sp.GetRequiredService<ILoadedSkillSet>()));
        services.AddSingleton<ICapabilitySource>(sp =>
            new WorkflowCapabilitySource(sp.GetRequiredService<IWorkflowRunner>()));
        services.AddSingleton<ITurnCatalog, RegisteredTurnCatalog>();
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
            sp.GetService<DatabaseInitializer>(),
            sp.GetRequiredService<ITokenCounter>(),
            sp.GetRequiredService<IContextLimitsProvider>(),
            sp.GetService<Core.Interfaces.IGameLogger>(),
            sp.GetRequiredService<ITurnCatalog>()));
        return services;
    }
}
