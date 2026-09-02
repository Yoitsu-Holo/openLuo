using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Mcp;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Infrastructure;
using openLuo.Composition;
using openLuo.Core.Models;
using openLuo.Abstractions;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Application.Runtime;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace openLuo.E2E.Tests;

public sealed class ExtensionCompositionIntegrationTests
{
    private sealed class StubCatalog : ICapabilityCatalog
    {
        public Task<CapabilityCatalogSnapshot> BuildSnapshotAsync(CatalogBuildContext context, CancellationToken ct = default)
            => Task.FromResult(new CapabilityCatalogSnapshot
            {
                ByCanonicalId = new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase)
            });
    }

    [Fact]
    public void ExtensionHost_LoadsAllFiveExtensions_WithDependencyResolution()
    {
        var root = FindRepositoryRoot();
        var extensionsRoot = Path.Combine(Path.GetTempPath(), $"openluo-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extensionsRoot);
        try
        {
            // 部署 5 个扩展的 manifest + 程序集（模拟 build.sh 的拷贝）
            foreach (var id in new[] { "memory", "companion", "world", "party" })
            {
                var dest = Path.Combine(extensionsRoot, id);
                Directory.CreateDirectory(dest);
                File.Copy(Path.Combine(root, "extensions", id, "extension.jsonc"), Path.Combine(dest, "extension.jsonc"));
                var dll = Path.Combine(root, "extensions", id, "bin", "Release", "net10.0", $"openLuo.Extension.{char.ToUpperInvariant(id[0]) + id[1..]}.dll");
                Assert.True(File.Exists(dll), $"extension assembly not found: {dll}");
                File.Copy(dll, Path.Combine(dest, Path.GetFileName(dll)));
            }

            // 提供扩展构造所需的宿主服务
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IMemoryRecallService, StubMemoryRecall>();
            services.AddSingleton<IMemoryWriteService, StubMemoryWrite>();
            services.AddSingleton<ILlmClient, StubLlmClient>();
            services.AddSingleton<IStateQueryService, StubStateQuery>();
            services.AddSingleton<IStateMutationService, StubStateMutation>();
            services.AddSingleton<IAgentRoster, StubRoster>();
            services.AddSingleton<IAgentRuntimeHub, StubRuntimeHub>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton(sp => new ExtensionRegistry(sp));
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<ExtensionRegistry>();
            var host = new ExtensionHost(extensionsRoot, type => Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(provider, type));
            var result = host.ScanAndLoad();

                        Assert.All(result.Diagnostics, d => Assert.True(d.Loaded, $"{d.ExtensionId}: {d.Error}"));
            Assert.Equal(4, result.Loaded.Count);
            registry.SetExtensions(result.Loaded);

            // 命名空间化后的能力目录
            var canonicalIds = registry.Capabilities.Select(c => c.CanonicalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("memory:search", canonicalIds);
            Assert.Contains("memory:write", canonicalIds);
            Assert.Contains("companion:chat", canonicalIds);
            Assert.Contains("world:state.read", canonicalIds);
            Assert.Contains("party:list_characters", canonicalIds);

            // 每个能力都有可解析的 invoker
            var invokers = registry.Invokers;
            foreach (var id in canonicalIds)
                Assert.True(invokers.ContainsKey(id), $"missing invoker for {id}");

            // contributor 实例可物化（memory:baseline 按类型注册）
            var contributors = registry.Contributors;
            Assert.Contains(contributors, c => c.Id == "memory:baseline");
            Assert.Contains(contributors, c => c.Id == "companion:identity");
        }
        finally
        {
            Directory.Delete(extensionsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Dispatcher_ResolvesCompanionChat_AfterRegistryFill()
    {
        var root = FindRepositoryRoot();
        var extensionsRoot = Path.Combine(Path.GetTempPath(), $"openluo-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extensionsRoot);
        try
        {
            foreach (var id in new[] { "memory", "companion", "world", "party" })
            {
                var dest = Path.Combine(extensionsRoot, id);
                Directory.CreateDirectory(dest);
                File.Copy(Path.Combine(root, "extensions", id, "extension.jsonc"), Path.Combine(dest, "extension.jsonc"));
                var dll = Path.Combine(root, "extensions", id, "bin", "Release", "net10.0", $"openLuo.Extension.{char.ToUpperInvariant(id[0]) + id[1..]}.dll");
                File.Copy(dll, Path.Combine(dest, Path.GetFileName(dll)));
            }

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IMemoryRecallService, StubMemoryRecall>();
            services.AddSingleton<IMemoryWriteService, StubMemoryWrite>();
            services.AddSingleton<ILlmClient, StubLlmClient>();
            services.AddSingleton<IStateQueryService, StubStateQuery>();
            services.AddSingleton<IStateMutationService, StubStateMutation>();
            services.AddSingleton<IAgentRoster, StubRoster>();
            services.AddSingleton<IAgentRuntimeHub, StubRuntimeHub>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton(sp => new ExtensionRegistry(sp));
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<ExtensionRegistry>();
            var host = new ExtensionHost(extensionsRoot, type => ActivatorUtilities.CreateInstance(provider, type));
            var result = host.ScanAndLoad();
            Assert.Equal(4, result.Loaded.Count);
            registry.SetExtensions(result.Loaded);

            // 组合根等价（ServiceCollectionExtensions 组合根 dispatcher 工厂的精确复制）：
            // canonicalInvokers = registry.Invokers + 远程 source invoker（本地无 MCP/A2A）
            var invokers = new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in registry.Invokers)
                invokers[pair.Key] = pair.Value;
            Assert.Contains("companion:chat", invokers.Keys);

            var catalog = new DefaultCapabilityCatalog([new ExtensionCapabilitySource(registry)]);
            catalog.LoadBase();
            var snapshot = await catalog.BuildSnapshotAsync(new CatalogBuildContext
            {
                SessionId = "s", SubjectId = "subject", TurnId = "t1", Budgets = DecisionBudgets.Default
            }, CancellationToken.None);

            var store = new InMemoryConversationStore();
            var sessions = new SessionStore();
            var assembler = new DefaultContextAssembler([]);
            var session = new DefaultAgentContextSession("s", "subject", assembler, store, new DefaultMessageTagPipeline());
            sessions.GetOrAdd("s", _ => session);

            var model = new ScriptedModel(
                new CapabilityDecision
                {
                    Calls =
                    [
                        new CapabilityCall
                        {
                            InvocationId = "inv-1", CanonicalId = "companion:chat",
                            ModelToolName = "companion:chat", Args = ["你好"], Options = new Dictionary<string, string>()
                        }
                    ]
                },
                new CapabilityDecision { Messages = [new FlowItem { Mode = FlowMode.Respond, Kind = ReplyItemKind.Text, Payload = "角色回复" }] });

            var updater = new SessionContextUpdater(sessions, new StubCatalog());
            var loop = new DefaultCapabilityDecisionLoop(
                model,
                new DefaultCapabilityDispatcher(
                    new UnboundCapabilityInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction(),
                    canonicalInvokers: invokers),
                updater, new SystemClock());

            var built = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
            {
                SessionId = "s", SubjectId = "subject", TurnId = "t1", UserInput = "你好"
            }, CancellationToken.None);
            var context = AgentContextConverter.ToDecisionContext(built, snapshot, DecisionBudgets.Default);

            var resultTurn = await loop.RunAsync(new DecisionLoopRequest
            {
                SessionId = "s", TurnId = "t1", SubjectId = "subject",
                Context = context, Catalog = snapshot, Budgets = DecisionBudgets.Default,
                BaseExecutionContext = new CapabilityExecutionContext
                {
                    GameId = "s", SessionId = "s", TurnId = "t1", SubjectId = "subject",
                    SnapshotVersion = built.SnapshotVersion,
                    DeadlineUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30),
                    OutputQueue = new InMemoryOutputQueue(),
                    SystemBlocks = built.Contributions.Select(c => $"[{c.Region}]\n{c.Content}\n[/{c.Region}]").ToList()
                }
            }, CancellationToken.None);

            Assert.True(resultTurn.Success);
            var outputText = resultTurn.Outputs.LastOrDefault(o => o.Kind == ReplyItemKind.Text)?.Payload?.ToString();
            Assert.Equal("ok", outputText);
            Assert.DoesNotContain("not bound to an invoker", outputText);
        }
        finally
        {
            Directory.Delete(extensionsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DispatcherFactory_WithMcpSource_KeepsExtensionInvokers()
    {
        var root = FindRepositoryRoot();
        var extensionsRoot = Path.Combine(Path.GetTempPath(), $"openluo-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extensionsRoot);
        try
        {
            foreach (var id in new[] { "memory", "companion", "world", "party" })
            {
                var dest = Path.Combine(extensionsRoot, id);
                Directory.CreateDirectory(dest);
                File.Copy(Path.Combine(root, "extensions", id, "extension.jsonc"), Path.Combine(dest, "extension.jsonc"));
                var dll = Path.Combine(root, "extensions", id, "bin", "Release", "net10.0", $"openLuo.Extension.{char.ToUpperInvariant(id[0]) + id[1..]}.dll");
                File.Copy(dll, Path.Combine(dest, Path.GetFileName(dll)));
            }

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IMemoryRecallService, StubMemoryRecall>();
            services.AddSingleton<IMemoryWriteService, StubMemoryWrite>();
            services.AddSingleton<ILlmClient, StubLlmClient>();
            services.AddSingleton<IStateQueryService, StubStateQuery>();
            services.AddSingleton<IStateMutationService, StubStateMutation>();
            services.AddSingleton<IAgentRoster, StubRoster>();
            services.AddSingleton<IAgentRuntimeHub, StubRuntimeHub>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton(sp => new ExtensionRegistry(sp));
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<ExtensionRegistry>();
            var host = new ExtensionHost(extensionsRoot, type => ActivatorUtilities.CreateInstance(provider, type));
            var result = host.ScanAndLoad();
            Assert.Equal(4, result.Loaded.Count);
            registry.SetExtensions(result.Loaded);
            var mcpServer = Path.Combine(FindRepositoryRoot(), "mcp", "media_server.py");

            var mcp = new McpCapabilitySource(new McpServerConfig
            {
                Id = "media", Transport = "stdio", Command = "python3", Args = [mcpServer]
            });
            // 真实 MCP source（stdio 启动 mcp/media_server.py），模拟生产组合根中的远程源
            await mcp.ConnectAsync(CancellationToken.None);
            Assert.True(mcp.IsHealthy, "MCP media server should connect (python3 + mcp package required)");

            // 组合根 dispatcher 工厂逐行（ServiceCollectionExtensions 130-152 的精确复制）
            var invokers = new Dictionary<string, ICapabilityInvoker>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in registry.Invokers)
                invokers[pair.Key] = pair.Value;
            foreach (var source in new ICapabilitySource[] { new ExtensionCapabilitySource(registry), mcp })
            {
                var descriptorInvoker = source switch
                {
                    McpCapabilitySource m when m.IsHealthy => (ICapabilityInvoker?)m.CreateInvoker(),
                    _ => null
                };
                if (descriptorInvoker is null)
                    continue;
                foreach (var descriptor in source.ListCapabilities())
                    invokers[descriptor.CanonicalId] = descriptorInvoker;
            }

            // MCP 源不能覆盖/丢失扩展 invoker
            Assert.Contains("companion:chat", invokers.Keys);
            Assert.Contains("mcp:media:fetch_random_image", invokers.Keys);
            Assert.Contains("memory:search", invokers.Keys);
            Assert.Contains("world:state.read", invokers.Keys);
            Assert.Contains("party:list_characters", invokers.Keys);
        }
        finally
        {
            Directory.Delete(extensionsRoot, recursive: true);
        }
    }
    [Fact]
    public void McpHeaderExpansion_EnvInline_Unset_Empty()
    {
        const string envVar = "OPENLUO_MCP_TEST_KEY";
        try
        {
            // 内嵌占位符：Authorization 值里的 {env:VAR} 被替换
            Environment.SetEnvironmentVariable(envVar, "sk-test-123");
            var expanded = McpCapabilitySource.ExpandHeaders(new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {{env:{envVar}}}",
                ["X-Custom"] = "plain"
            });
            Assert.Equal("Bearer sk-test-123", expanded["Authorization"]);
            Assert.Equal("plain", expanded["X-Custom"]);

            // 未设置的变量保留原样（连接失败警告会暴露该问题）
            Environment.SetEnvironmentVariable(envVar, null);
            var unresolved = McpCapabilitySource.ExpandHeaders(new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {{env:{envVar}}}"
            });
            Assert.Equal($"Bearer {{env:{envVar}}}", unresolved["Authorization"]);

            // 空 headers 不产生任何头
            Assert.Empty(McpCapabilitySource.ExpandHeaders(new Dictionary<string, string>()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    private static string LocateExtensionAssembly(string root, string id)
    {
        var pattern = Path.Combine(root, "extensions", id, "bin", "*", "net10.0", "openLuo.Extension.*.dll");
        return Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(pattern))!)!, "openLuo.Extension.*.dll", SearchOption.AllDirectories)
            .First(p => p.Contains(Path.Combine("bin", "Debug")) || p.Contains(Path.Combine("bin", "Release")));
    }

    private sealed class InMemoryConversationStore : IConversationStore
    {
        private readonly List<ConversationTurn> _turns = [];
        public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationTurn>>(_turns.Where(t => t.SessionId == sessionId).TakeLast(limit).ToList());
        public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default)
        {
            _turns.Add(turn);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedModel : ICapabilityDecisionModel
    {
        private readonly Queue<CapabilityDecision> _queue;
        public ScriptedModel(params CapabilityDecision[] decisions) => _queue = new Queue<CapabilityDecision>(decisions);
        public Task<CapabilityDecision> DecideAsync(CapabilityDecisionContext context, CancellationToken ct = default) =>
            Task.FromResult(_queue.Dequeue());
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "openLuo.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("openLuo.slnx");
    }

    private sealed class StubMemoryRecall : IMemoryRecallService
    {
        public Task<MemoryRecallResult> RecallAsync(SemanticRecallQuery query, CancellationToken ct = default) =>
            Task.FromResult(new MemoryRecallResult { Success = true, Summary = "stub" });
    }
    private sealed class StubMemoryWrite : IMemoryWriteService
    {
        public Task<MemoryWriteResult> WriteAsync(MemoryWriteInput input, CancellationToken ct = default) =>
            Task.FromResult(new MemoryWriteResult { Success = true, MemoryId = "m1" });
    }
    private sealed class StubLlmClient : ILlmClient
    {
        public Task<LlmChatResponse> CompleteAsync(IEnumerable<ChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult(new LlmChatResponse { Content = "ok" });
        public Task<string> StreamAsync(IEnumerable<ChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default) =>
            Task.FromResult("ok");
    }
    private sealed class StubStateQuery : IStateQueryService
    {
        public Task<StateValue> GetAsync(string gameId, string @namespace, StateOwnerKind ownerKind, string ownerId, string key) =>
            Task.FromResult(new StateValue { Key = key, Value = "0" });
        public Task<List<StateValue>> QueryAsync(string gameId, string? @namespace, StateOwnerKind? ownerKind, string? ownerId, IEnumerable<string>? keys = null, bool includeDefaults = false) =>
            Task.FromResult(new List<StateValue>());
    }
    private sealed class StubStateMutation : IStateMutationService
    {
        public Task<List<StateMutationResult>> ApplyAsync(string gameId, IEnumerable<StateMutation> mutations) =>
            Task.FromResult(new List<StateMutationResult>());
    }
    private sealed class StubRoster : IAgentRoster
    {
        public Task<IReadOnlyList<Character>> ListAsync(string gameId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Character>>([]);
        public Task<Character?> ResolveAsync(string gameId, string selector, CancellationToken ct = default) => Task.FromResult<Character?>(null);
        public Task<Character?> GetActiveAsync(GameState state, CancellationToken ct = default) => Task.FromResult<Character?>(null);
        public Task<Character?> SetActiveAsync(string gameId, string selector, CancellationToken ct = default) => Task.FromResult<Character?>(null);
    }
    private sealed class StubRuntimeHub : IAgentRuntimeHub
    {
        public Task<AgentMessage?> RequestAsync(string characterId, AgentMessageType type, string from, string payload, string gameId, string? correlationId = null, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult<AgentMessage?>(new AgentMessage(Guid.NewGuid().ToString("N"), gameId, characterId, from, AgentMessageType.AgentReply, "stub reply"));
    }
}
