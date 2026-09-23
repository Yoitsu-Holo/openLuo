using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Capabilities.Mcp;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using openLuo.Composition;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>工具调用协议流：assistant tool_calls → tool 结果消息（OpenAI 兼容要求）。</summary>
public sealed class ToolProtocolFlowTests
{
    private sealed class StubCatalog : ICapabilityCatalog
    {
        public Task<CapabilityCatalogSnapshot> BuildSnapshotAsync(CatalogBuildContext context, CancellationToken ct = default)
            => Task.FromResult(new CapabilityCatalogSnapshot
            {
                ByCanonicalId = new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase)
            });
    }

    private sealed class MemoryConversationStore : IConversationStore
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

    private sealed class SequenceModel : ICapabilityDecisionModel
    {
        private readonly Queue<CapabilityDecision> _queue;
        public SequenceModel(params CapabilityDecision[] decisions) => _queue = new Queue<CapabilityDecision>(decisions);
        public Task<CapabilityDecision> DecideAsync(CapabilityDecisionContext context, CancellationToken ct = default) =>
            Task.FromResult(_queue.Dequeue());
    }

    private sealed class StubInvoker : ICapabilityInvoker
    {
        public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
            Task.FromResult(new CapabilityResult
            {
                InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Failed,
                Text = null, Error = "media source did not return an image"
            });
    }

    [Fact]
    public async Task ToolRoundtrip_SecondDecision_ContainsAssistantToolCallsAndToolMessage()
    {
        var catalog = new DefaultCapabilityCatalog([]);
        catalog.LoadBase();
        var store = new MemoryConversationStore();
        var sessions = new SessionStore();
        var assembler = new DefaultContextAssembler([]);
        var session = new DefaultAgentContextSession("s", "subject", assembler, store, new DefaultMessageTagPipeline());
        sessions.GetOrAdd("s", _ => session);

        var model = new SequenceModel(
            new CapabilityDecision
            {
                Calls =
                [
                    new CapabilityCall
                    {
                        InvocationId = "inv-1", ModelCallId = "call_00_abc",
                        ModelToolName = "cap_media_fetch_x", RawArgumentsJson = "{\"args\":[],\"options\":{}}",
                        CanonicalId = "media:fetch_random_image"
                    }
                ]
            },
            new CapabilityDecision { Messages = [new FlowItem { Mode = FlowMode.Respond, Kind = ReplyItemKind.Text, Payload = "图源没返回图片，我换一种方式。" }] });

        var updater = new SessionContextUpdater(sessions, new StubCatalog());
        var loop = new DefaultCapabilityDecisionLoop(
            model,
            new DefaultCapabilityDispatcher(new StubInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction()),
            updater, new SystemClock());

        var built = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "t1", UserInput = "来张图看看"
        }, CancellationToken.None);
        var context = AgentContextConverter.ToDecisionContext(built, catalog: null, DecisionBudgets.Default);

        var result = await loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = "s", TurnId = "t1", SubjectId = "subject",
            Context = context,
            Catalog = new CapabilityCatalogSnapshot
            {
                ByCanonicalId = new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    ["media:fetch_random_image"] = new CapabilityDescriptor
                    {
                        CanonicalId = "media:fetch_random_image", ModelToolName = "cap_media_fetch_x",
                        Kind = CapabilityKind.Builtin, ProviderId = "media", SideEffect = SideEffectClass.ReadOnly,
                        Completion = CompletionPolicy.Continue
                    }
                },
                ModelNameToCanonicalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cap_media_fetch_x"] = "media:fetch_random_image"
                }
            },
            Budgets = DecisionBudgets.Default,
            BaseExecutionContext = new CapabilityExecutionContext { SessionId = "s", TurnId = "t1", SubjectId = "subject" }
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("图源没返回图片，我换一种方式。", result.FinalText);

        // 第二次决策的上下文对话流：user → assistant(tool_calls) → tool
        var conversation = session.Current.Conversation;
        Assert.Equal(2, conversation.Count);
        Assert.Equal("assistant", conversation[0].SpeakerRole);
        Assert.Contains("call_00_abc", conversation[0].ToolCallsJson);
        Assert.Equal("tool", conversation[1].SpeakerRole);
        Assert.Equal("call_00_abc", conversation[1].ToolCallId);
    }
    [Theory]
    [InlineData("[TIME: 2026/08/17 16:58:02] [TYPE: text]  我看看日历。", "我看看日历。")]
    [InlineData("[2026/08/17 16:58:43] [TYPE: text]  行啊，七夕一起过吧。", "行啊，七夕一起过吧。")]
    [InlineData("[TIME: 2026/08/17 16:58:02] [TYPE: text] [FROM: assistant] 完整前缀。", "完整前缀。")]
    [InlineData("  你好，正常回复。", "你好，正常回复。")]
    [InlineData("正常文本不带任何标记。", "正常文本不带任何标记。")]
    public void SanitizeMetadataPrefix_StripsModelImitationPrefixes(string input, string expected)
    {
        Assert.Equal(expected, AgentContextConverter.SanitizeMetadataPrefix(input));
    }
    [Theory]
    [InlineData("<tool_calls>\n<invoke name=\"cap_mcp12306get-tickets_dc018d99\">\n<parameter name=\"args\">{\"from\":\"深圳北\"}</parameter>\n</invoke>\n</tool_calls>\n｜｜DSML\n我这就查。", "我这就查。")]
    [InlineData("（我轻轻抬袖）稍等，我来查。\n\n<tool_calls>\n<invoke name=\"cap_x\">\n<parameter>a</parameter>\n</invoke>\n</tool_calls>", "（我轻轻抬袖）稍等，我来查。")]
    [InlineData("开头文本</tool_calls>残留标签", "开头文本残留标签")]
    [InlineData("纯文本回复，无任何工具标记。", "纯文本回复，无任何工具标记。")]
    [InlineData("<｜｜tool_calls>\n<｜｜invoke name=\"cap_mcp12306get-station-code-by-names_dc018d99\">\n<｜｜parameter name=\"stationNames\" string=\"true\">深圳北,长沙南</｜｜parameter>\n</｜｜invoke>\n</｜｜tool_calls>", "")]
    [InlineData("我先确认一下车站代码，稍等。\n\n<｜｜tool_calls>\n<｜｜invoke name=\"cap_mcp12306get-station-code-by-names_dc018d99\">\n<｜｜parameter name=\"stationNames\" string=\"true\">深圳北,长沙南</｜｜parameter>\n</｜｜invoke>\n</｜｜tool_calls>", "我先确认一下车站代码，稍等。")]
    [InlineData("<｜｜tool_calls>", "")]
    [InlineData("｜｜DSML 结尾", "结尾")]
    public void SanitizeMetadataPrefix_StripsXmlToolCallBlocks(string input, string expected)
    {
        Assert.Equal(expected, AgentContextConverter.SanitizeMetadataPrefix(input));
    }

    [Fact]
    public async Task ChatLineFormatting_AssistantBare_UserFromOnly()
    {
        var store = new MemoryConversationStore();
        await store.AppendAsync(new ConversationTurn
        {
            SessionId = "s2", TurnId = "t0:user", SpeakerId = "Yoitsu_Holo", SpeakerRole = "user",
            Content = "在吗？", TimestampUtc = DateTimeOffset.UtcNow
        });
        await store.AppendAsync(new ConversationTurn
        {
            SessionId = "s2", TurnId = "t0:assistant", SpeakerId = "subject", SpeakerRole = "outbound",
            Content = "在的，怎么了？", TimestampUtc = DateTimeOffset.UtcNow
        });
        await store.AppendAsync(new ConversationTurn
        {
            SessionId = "s2", TurnId = "t1:user", SpeakerId = "Yoitsu_Holo", SpeakerRole = "user",
            Content = "今天天气怎么样？", TimestampUtc = DateTimeOffset.UtcNow
        });

        var sessions = new SessionStore();
        var assembler = new DefaultContextAssembler([]);
        var session = new DefaultAgentContextSession("s2", "subject", assembler, store, new DefaultMessageTagPipeline());
        sessions.GetOrAdd("s2", _ => session);
        var built = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s2", SubjectId = "subject", TurnId = "t1", UserInput = "今天天气怎么样？"
        }, CancellationToken.None);
        var context = AgentContextConverter.ToDecisionContext(built, catalog: null, DecisionBudgets.Default);

        // user 消息：仅 [FROM:] 标注，无逐条 [TIME:]/[TYPE: text]（注入面收缩）
        var userMsg = context.Conversation.Last(m => m.Role == "user");
        Assert.Equal("[FROM: Yoitsu_Holo] 今天天气怎么样？", userMsg.Content);
        // assistant 消息：纯内容，无任何元信息前缀（消除模仿模板）
        var assistantMsg = context.Conversation.First(m => m.Role == "assistant");
        Assert.Equal("在的，怎么了？", assistantMsg.Content);
    }

    [Fact]
    public async Task McpInputSchema_ReachesToolParameters_Real12306()
    {
        // 生产同款 12306 streamable-http MCP：工具 schema 必须穿透到决策模型的
        // LlmToolSpec.Parameters（否则模型拿不到 required 参数名，产生 -32602）。
        var source = new McpCapabilitySource(new McpServerConfig
        {
            Id = "12306",
            Transport = "streamable-http",
            Url = "https://mcp.api-inference.modelscope.net/303e117d05c441/mcp",
            InjectContextKeys = false
        });
        try
        {
            await source.ConnectAsync(CancellationToken.None);
            if (!source.IsHealthy)
                return; // 网络不可达时跳过，不视为失败

            var descriptor = source.ListCapabilities()
                .FirstOrDefault(d => d.CanonicalId == "mcp:12306:get-station-code-of-citys");
            Assert.NotNull(descriptor);

            // 复刻 LlmCapabilityDecisionModel 的 Parameters 转换（JsonElement 装箱路径）
            var parameters = descriptor.InputSchema switch
            {
                JsonObject obj => obj,
                JsonNode node => node.AsObject(),
                null => null,
                var other => JsonSerializer.SerializeToNode(other)?.AsObject()
            };
            Assert.NotNull(parameters);
            var json = parameters!.ToJsonString();
            Assert.Contains("\"citys\"", json);
            Assert.Contains("\"required\"", json);
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    [Fact]
    public async Task McpInvoker_PassesNativeArguments_Real12306()
    {
        // 回归：模型原生 tool_calls 的 RawArgumentsJson（顶层键 = MCP 参数）必须
        // 原样传给 MCP 服务端——此前 ParseOptions 只认 {"options":{...}} 包装，
        // 导致 {"stationNames":"深圳北"} 被丢弃，12306 返回 -32602 Required at stationNames。
        var source = new McpCapabilitySource(new McpServerConfig
        {
            Id = "12306",
            Transport = "streamable-http",
            Url = "https://mcp.api-inference.modelscope.net/303e117d05c441/mcp",
            InjectContextKeys = false
        });
        try
        {
            await source.ConnectAsync(CancellationToken.None);
            if (!source.IsHealthy)
                return; // 网络不可达时跳过

            var invoker = source.CreateInvoker();
            var result = await invoker.InvokeAsync(new CapabilityCall
            {
                InvocationId = Guid.NewGuid().ToString("N"),
                CanonicalId = "mcp:12306:get-station-code-by-names",
                ModelToolName = "cap_mcp12306get-station-code-by-names_dc018d99",
                RawArgumentsJson = """{"stationNames":"深圳北|长沙"}"""
            }, new CapabilityExecutionContext
            {
                GameId = "test", SessionId = "test", TurnId = "t1", SubjectId = "test"
            }, CancellationToken.None);

            Assert.True(result.Success, $"12306 should accept native args, got: {result.Error}");
            Assert.Contains("station", result.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await source.DisposeAsync();
        }
    }
}
