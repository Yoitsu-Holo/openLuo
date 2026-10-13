using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Llm;
using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Tests.Llm;

/// <summary>
/// “配置真值”回归（切片 A）：调用方未指定 temperature / maxTokens 时必须回落到 route 配置。
/// 历史缺陷：LlmOptions 把“未指定”写成了硬编码默认（0.5f / 4096），
/// RuntimeConfiguredLlmClient 又把 null options 换成 new LlmOptions()，
/// 于是 LlmClientBase.MergeOptions 里的 `?? route 值` 永远命中硬编码，route 配置形同虚设。
/// 断言口径取自真实 HTTP 请求体（覆盖 ILlmClient → 路由 → LlmClientBase.MergeOptions → BuildBody
/// 的完整链路），而不是只对比内部对象——只有落到请求体上才算“真值”。
/// </summary>
public sealed class LlmRequestOptionTruthTests
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

    private static LlmConfig RouteConfig(string baseUrl, float temperature, int? maxTokens) => new()
    {
        Routes =
        [
            new LlmRouteConfig
            {
                Name = "truth-route",
                Enabled = true,
                Provider = LlmProvider.OpenAI,
                BaseUrl = baseUrl,
                Model = "truth-model",
                Temperature = temperature,
                MaxTokens = maxTokens,
                MaxRetryAttempts = 0,   // 断言失败要立刻暴露，别让重试+退避掩盖
                TimeoutSeconds = 10,
                Capabilities = new LlmCapabilitiesConfig { SupportsJsonMode = true, SupportsTools = true }
            }
        ]
    };

    /// <summary>经真实 ILlmClient 链路发一次请求，返回实际送出的 JSON 请求体。</summary>
    private static async Task<JsonObject> CaptureBodyAsync(float routeTemperature, int? routeMaxTokens, LlmOptions? options)
    {
        using var server = new RecordingServer();
        var client = new RuntimeConfiguredLlmClient(() => RouteConfig(server.BaseUrl, routeTemperature, routeMaxTokens));
        await client.CompleteAsync([new LocalChatMessage(ChatMessageRole.User, "你好")], options);
        return server.LastBody;
    }

    [Fact]
    public async Task OptionsNull_RouteTemperatureAndMaxTokensReachRequestBody()
    {
        var body = await CaptureBodyAsync(routeTemperature: 0.15f, routeMaxTokens: 777, options: null);

        Assert.Equal(0.15, body["temperature"]!.GetValue<double>(), 3);
        Assert.Equal(777, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task ExplicitTemperatureWins_UnsetMaxTokensStillFallsBackToRoute()
    {
        var body = await CaptureBodyAsync(routeTemperature: 0.15f, routeMaxTokens: 777, new LlmOptions { Temperature = 0.2f });

        Assert.Equal(0.2, body["temperature"]!.GetValue<double>(), 3);
        Assert.Equal(777, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task ExplicitNullTemperature_FallsBackToRoute()
    {
        // “显式 null”与“未设置”等价：都表示交路由，不是“用 0”也不是“用硬编码默认”
        var body = await CaptureBodyAsync(routeTemperature: 0.15f, routeMaxTokens: 777, new LlmOptions { Temperature = null, MaxTokens = null });

        Assert.Equal(0.15, body["temperature"]!.GetValue<double>(), 3);
        Assert.Equal(777, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task RouteMaxTokensNull_OmitsMaxTokensField()
    {
        // 线上 llm.jsonc 形态：temperature=0.5、maxTokens=null（null 表示“不覆盖”→ 请求体里不出现 max_tokens）
        var body = await CaptureBodyAsync(routeTemperature: 0.5f, routeMaxTokens: null, options: null);

        Assert.Equal(0.5, body["temperature"]!.GetValue<double>(), 3);
        Assert.False(body.ContainsKey("max_tokens"));
    }

    [Fact]
    public async Task ExplicitZeroTemperature_IsNotTreatedAsUnset()
    {
        // LlmRouteDecider 显式设 0f（路由推理要最确定输出）——0 是有效值，不能被回落成 route 温度
        var body = await CaptureBodyAsync(routeTemperature: 0.15f, routeMaxTokens: null, new LlmOptions { Temperature = 0f });

        Assert.Equal(0.0, body["temperature"]!.GetValue<double>(), 3);
    }

    [Fact]
    public void LlmOptions_SamplingFieldsDefaultToUnspecified()
    {
        var options = new LlmOptions();

        Assert.Null(options.Temperature);
        Assert.Null(options.MaxTokens);
    }

    [Fact]
    public void LlmOptionsFactory_LeavesSamplingToRoute_KeepsMultimodalSemantics()
    {
        var factory = LlmOptionsFactory.Default;

        var textOnly = factory.Build(new CapabilityDecisionContext { UserInput = "你好" });
        Assert.Null(textOnly.Temperature);      // 不再硬编码 0.7f —— 交给 route 配置
        Assert.Null(textOnly.MaxTokens);        // 不再硬编码 2048 —— 交给 route 配置
        Assert.False(textOnly.EnableMultimodal);
        Assert.False(textOnly.JsonMode);

        var withImage = factory.Build(new CapabilityDecisionContext
        {
            UserInput = "看看这张图",
            UserBlocks = [new ImageBlock { Kind = BlockKind.Image, AssetId = "u1", MimeType = "image/png" }]
        });
        Assert.True(withImage.EnableMultimodal);   // 多模态开关语义保持不变
    }
}
