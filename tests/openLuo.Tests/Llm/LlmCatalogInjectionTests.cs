using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Llm;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat;

namespace openLuo.Tests.Llm;

/// <summary>
/// 目录注入回归：技能 / 工作流 / 远程 Agent 的摘要目录必须真的落到**送出的 HTTP 请求体**里
/// （Skills / Workflows / RemoteAgents 三个字段此前从未被 LLM 适配层读取，模型看不到任何技能或工作流）。
/// 断言口径取自请求体的 messages（覆盖 ILlmClient → 路由 → LlmClientBase → BuildBody 的完整链路），
/// 而不是只看内部对象——只有模型真的收到那几行文本才算接线成功。
/// </summary>
public sealed class LlmCatalogInjectionTests
{
    // ── 记录请求体的最小 HTTP 服务：走真实 socket，不 mock 掉序列化环节 ──

    private sealed class RecordingServer : IDisposable
    {
        private const string ResponseBody =
            """{"id":"t","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";

        private readonly HttpListener _listener = new();
        private readonly List<JsonObject> _bodies = [];
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string BaseUrl { get; }

        public RecordingServer()
        {
            var port = FreeTcpPort();
            BaseUrl = $"http://127.0.0.1:{port}/v1/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        /// <summary>最后一次请求体（每个用例只发一次请求）。</summary>
        public JsonObject LastBody
        {
            get { lock (_gate) return _bodies[^1]; }
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return; // Stop/Close 唤醒，属正常收尾
                }

                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    var node = JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
                    lock (_gate) _bodies.Add(node);
                }

                var payload = Encoding.UTF8.GetBytes(ResponseBody);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        private static int FreeTcpPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // 已释放/未启动：忽略
            }

            try
            {
                _loop.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // 关停竞态：忽略
            }

            _cts.Dispose();
        }
    }

    private static LlmConfig RouteConfig(string baseUrl) => new()
    {
        Routes =
        [
            new LlmRouteConfig
            {
                Name = "catalog-route",
                Enabled = true,
                Provider = LlmProvider.OpenAI,
                BaseUrl = baseUrl,
                Model = "catalog-model",
                MaxRetryAttempts = 0,   // 断言失败要立刻暴露，别让重试+退避掩盖
                TimeoutSeconds = 10,
                Capabilities = new LlmCapabilitiesConfig { SupportsJsonMode = true, SupportsTools = true }
            }
        ]
    };

    /// <summary>经真实 ILlmClient 链路发一次请求，返回实际送出的 JSON 请求体。</summary>
    private static async Task<JsonObject> CaptureBodyAsync(CapabilityDecisionContext context)
    {
        using var server = new RecordingServer();
        var client = new RuntimeConfiguredLlmClient(() => RouteConfig(server.BaseUrl));
        var model = new LlmCapabilityDecisionModel(client);
        await model.DecideAsync(context, CancellationToken.None);
        return server.LastBody;
    }

    /// <summary>请求体里所有消息的文本（块内容为数组时按原样 JSON，不做文本拼接）。</summary>
    private static List<(string Role, string Text)> Messages(JsonObject body) =>
        body["messages"]!.AsArray()
            .Select(node => node!.AsObject())
            .Select(message => (
                message["role"]!.GetValue<string>(),
                message["content"] is JsonValue value ? value.GetValue<string>() : message["content"]!.ToJsonString()))
            .ToList();

    /// <summary>某条消息是否携带以 <paramref name="tag"/> 开头的 [TAG] 块（块首行即标签）。</summary>
    private static bool HasBlock(JsonObject body, string tag) =>
        Messages(body).Any(message =>
            message.Role == "system"
            && message.Text.Split('\n').Any(line => line.Trim() == tag));

    private static string CatalogText(JsonObject body, string tag) =>
        Messages(body).Single(message => message.Role == "system" && message.Text.Split('\n').Any(l => l.Trim() == tag)).Text;

    private static CapabilitySummary Capability(string canonicalId, string modelToolName) => new()
    {
        CanonicalId = canonicalId,
        ModelToolName = modelToolName,
        DisplayName = canonicalId,
        Summary = "摘要",
        InputSchema = new JsonObject { ["type"] = "object" }
    };

    private static CapabilityDecisionContext BuildContext(
        IReadOnlyList<SkillSummary>? skills = null,
        IReadOnlyList<WorkflowSummary>? workflows = null,
        IReadOnlyList<RemoteAgentSummary>? remoteAgents = null,
        IReadOnlyList<CapabilitySummary>? capabilities = null) => new()
    {
        SessionId = "s1", TurnId = "t1", SnapshotVersion = 1,
        SystemBlocks = ["[Identity]\n角色\n[/Identity]"],
        Conversation = [new ContextMessage { Role = "user", Content = "历史消息" }],
        UserInput = "帮我总结这个议题",
        Capabilities = capabilities ?? [],
        Skills = skills ?? [],
        Workflows = workflows ?? [],
        RemoteAgents = remoteAgents ?? []
    };

    [Fact]
    public async Task SkillsAndWorkflows_ReachRequestBodyAsTaggedBlocks()
    {
        // 真实函数名由目录快照生成（cap_<safe>_<hash>），这里手写一个与 GenerateModelToolName 同形的名字，
        // 用于断言目录块指引的是模型唯一能调的名，而不是 canonical id 字面量。
        var body = await CaptureBodyAsync(BuildContext(
            capabilities: [Capability("core:load_skill", "cap_coreload_skill_ab12cd34")],
            skills:
            [
                new SkillSummary { Id = "render_to_png", Title = "渲染图片", Summary = "把 HTML 渲染成 PNG", WhenToUse = "用户要求出图时" },
                new SkillSummary { Id = "diff_review", Title = "评审差异", Summary = "逐条评审代码差异" }
            ],
            workflows: [new WorkflowSummary { Id = "weekly_report", Description = "收集本周进展并生成周报" }],
            remoteAgents: [new RemoteAgentSummary { Id = "a2a:peer:summarize", DisplayName = "摘要小助手", Description = "远程文档摘要" }]));

        var skills = CatalogText(body, "[Skills]");
        Assert.EndsWith("[/Skills]", skills);
        Assert.Contains("- render_to_png: 渲染图片。适用时机：用户要求出图时", skills);
        // WhenToUse 缺失时退回 Summary：目录的价值就是告诉模型“什么时候用得上”
        Assert.Contains("- diff_review: 评审差异。适用时机：逐条评审代码差异", skills);
        Assert.Contains("cap_coreload_skill_ab12cd34（canonical id core:load_skill）", skills);
        Assert.Contains("{\"id\":\"<skillId>\"}", skills);

        var workflows = CatalogText(body, "[Workflows]");
        Assert.EndsWith("[/Workflows]", workflows);
        Assert.Contains("- weekly_report: 收集本周进展并生成周报", workflows);
        // 工作流能力未在本轮目录里 → 指引退回 canonical id（能力被权限过滤时仍能指向正确名字）
        Assert.Contains("core:run_workflow", workflows);
        // 输入走扁平命名参数（嵌套对象过不了 wire）——指引必须与能力的实际参数形状一致
        Assert.Contains("参数 {\"id\":\"<workflowId>\"}", workflows);
        Assert.Contains("平铺传入", workflows);

        var agents = CatalogText(body, "[RemoteAgents]");
        Assert.EndsWith("[/RemoteAgents]", agents);
        Assert.Contains("- a2a:peer:summarize (摘要小助手)：远程文档摘要", agents);

        // 目录块明确自述“只有摘要”：正文按需加载，不进每轮上下文
        Assert.Contains("这里只有摘要", skills);
    }

    [Fact]
    public async Task CatalogBlocks_PrecedeConversationAndKeepKernelPromptFirst()
    {
        var body = await CaptureBodyAsync(BuildContext(
            skills: [new SkillSummary { Id = "render_to_png", Title = "渲染图片", WhenToUse = "用户要求出图时" }]));

        var messages = Messages(body);
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("You are running inside the openLuo agent kernel", messages[0].Text);

        var skillsIndex = messages.FindIndex(m => m.Text.Split('\n').Any(l => l.Trim() == "[Skills]"));
        var userIndex = messages.FindIndex(m => m.Text.Contains("帮我总结这个议题"));
        // 目录必须早于用户输入：模型读到问题前就得知道有哪些技能可加载
        Assert.True(skillsIndex > 0 && skillsIndex < userIndex, $"skillsIndex={skillsIndex}, userIndex={userIndex}");
    }

    [Fact]
    public async Task EmptyDirectories_ProduceNoCatalogBlocks()
    {
        var body = await CaptureBodyAsync(BuildContext());

        Assert.False(HasBlock(body, "[Skills]"));
        Assert.False(HasBlock(body, "[Workflows]"));
        Assert.False(HasBlock(body, "[RemoteAgents]"));
        Assert.DoesNotContain("[Skills]", Messages(body).Select(m => m.Text));
    }

    [Fact]
    public async Task CatalogHintFallsBackToCanonicalIdWhenCapabilityAbsent()
    {
        var body = await CaptureBodyAsync(BuildContext(
            skills: [new SkillSummary { Id = "render_to_png", Title = "渲染图片" }],
            workflows: [new WorkflowSummary { Id = "weekly_report", Description = "周报" }]));

        Assert.Contains("调用工具 core:load_skill，参数 {\"id\":\"<skillId>\"}", CatalogText(body, "[Skills]"));
        // 引用方案的配套指引：加载只登记引用、用完可卸载（都是同一批 canonical id 回退）
        Assert.Contains("core:unload_skill", CatalogText(body, "[Skills]"));
        Assert.Contains("调用工具 core:run_workflow", CatalogText(body, "[Workflows]"));
    }

    [Fact]
    public void KernelPrompt_ListsCatalogTags()
    {
        // 标签清单是模型唯一的结构说明书；不登记 = 模型把 [Skills] 当用户内容或未知噪声
        foreach (var tag in new[] { "Skills", "Workflows", "RemoteAgents" })
            Assert.Contains(tag, KernelPrompt.Content);
        Assert.Contains("catalogs of summaries only", KernelPrompt.Content);
    }
}
