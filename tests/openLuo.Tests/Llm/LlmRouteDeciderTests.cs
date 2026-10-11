using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Tests.Llm;

public sealed class LlmRouteDeciderTests
{
    private static LlmConfig TwoRouteConfig(string routingMode = "llm") => new()
    {
        Routes =
        [
            new LlmRouteConfig
            {
                Name = "deepseek-fast-text", Enabled = true, Priority = 100, Provider = LlmProvider.DeepSeek,
                Model = "deepseek-v4-flash",
                Capabilities = new LlmCapabilitiesConfig { SupportsVision = false, SupportsTools = true }
            },
            new LlmRouteConfig
            {
                Name = "deepseek-fast-vision", Enabled = true, Priority = 90, Provider = LlmProvider.DeepSeek,
                Model = "deepseek-v4-flash-vision-exp",
                Capabilities = new LlmCapabilitiesConfig { SupportsVision = true, SupportsTools = true }
            },
            new LlmRouteConfig
            {
                Name = "qwen-vision", Enabled = true, Priority = 80, Provider = LlmProvider.Qwen,
                Model = "qwen-vl-plus",
                Capabilities = new LlmCapabilitiesConfig { SupportsVision = true, SupportsTools = true }
            }
        ],
        Routing = new LlmRoutingConfig { Mode = routingMode, Model = "deepseek-fast-text", MaxTokens = 256, TimeoutSeconds = 8 }
    };

    private sealed class FakeLlmClient : ILlmClient
    {
        public int CallCount { get; private set; }
        public string ResponseContent { get; set; } = "{}";
        public Exception? ThrowOnCall { get; set; }
        public IReadOnlyList<LocalChatMessage>? LastMessages { get; private set; }

        public Task<LlmChatResponse> CompleteAsync(IEnumerable<LocalChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default)
        {
            CallCount++;
            LastMessages = messages.ToList();
            if (ThrowOnCall is not null)
                return Task.FromException<LlmChatResponse>(ThrowOnCall);
            return Task.FromResult(new LlmChatResponse { Content = ResponseContent });
        }

        public Task<string> StreamAsync(IEnumerable<LocalChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static LlmRouteDecider CreateDecider(LlmConfig config, FakeLlmClient fake) =>
        new(() => config, _ => fake);

    private static LocalChatMessage TextMessage(string text) => new(ChatMessageRole.User, text);

    private static LocalChatMessage ImageMessage(string text) => new(ChatMessageRole.User, text)
    {
        Blocks = [new ImageBlock { Kind = BlockKind.Image, AssetId = "a1", MimeType = "image/png", DataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==" }]
    };

    [Fact]
    public async Task SingleCandidate_ShortCircuitsWithoutRouterCall()
    {
        var config = TwoRouteConfig();
        config.Routes[1].Enabled = false;
        config.Routes[2].Enabled = false; // 只剩文本 deepseek
        var fake = new FakeLlmClient();
        var decider = CreateDecider(config, fake);

        var decision = await decider.DecideAsync([TextMessage("你好")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-text", decision.Route.Name);
        Assert.False(decision.EnableMultimodal);
        Assert.Equal(0, fake.CallCount); // 候选唯一：零 LLM 调用
    }

    [Fact]
    public async Task NoImage_PicksNonVisionRoute_WithoutRouterCall()
    {
        var fake = new FakeLlmClient();
        var decider = CreateDecider(TwoRouteConfig(), fake);

        var decision = await decider.DecideAsync([TextMessage("今天天气如何")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-text", decision.Route.Name); // 无图：非视觉优先
        Assert.False(decision.EnableMultimodal);
        Assert.Equal(0, fake.CallCount); // 无图：零路由推理
    }

    [Fact]
    public async Task NoImage_AllVisionCandidates_FallsBackToHighestPriority()
    {
        var config = TwoRouteConfig();
        config.Routes[0].Enabled = false; // 只剩两个视觉模型
        var fake = new FakeLlmClient();
        var decider = CreateDecider(config, fake);

        var decision = await decider.DecideAsync([TextMessage("你好")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-vision", decision.Route.Name); // priority 90 > 80
        Assert.False(decision.EnableMultimodal); // 但无图不上线
    }

    [Fact]
    public async Task ExplicitVisionRequirement_SkipsRouterAndPicksVision()
    {
        var fake = new FakeLlmClient();
        var decider = CreateDecider(TwoRouteConfig(), fake);
        var options = new LlmOptions { RequiredCapabilities = new RequiredLlmCapabilities { Vision = true } };

        var decision = await decider.DecideAsync([TextMessage("hi")], options, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-vision", decision.Route.Name);
        Assert.True(decision.EnableMultimodal);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Image_RouterSaysYes_PicksVisionRoute()
    {
        var fake = new FakeLlmClient { ResponseContent = """{"multimodal": true}""" };
        var decider = CreateDecider(TwoRouteConfig(), fake);

        var decision = await decider.DecideAsync([ImageMessage("看看这个图")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-vision", decision.Route.Name); // 视觉候选 priority 90 最高
        Assert.True(decision.EnableMultimodal);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Image_RouterSaysNo_PicksTextRoute_AndDisablesMultimodal()
    {
        var fake = new FakeLlmClient { ResponseContent = """{"multimodal": false}""" };
        var decider = CreateDecider(TwoRouteConfig(), fake);

        var decision = await decider.DecideAsync([ImageMessage("今天天气如何")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-text", decision.Route.Name);
        Assert.False(decision.EnableMultimodal); // 判不需要识图：图不上线
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Image_RouterGarbage_FallsBackToVision()
    {
        var fake = new FakeLlmClient { ResponseContent = "```json\n{\"multimodal\": true}\n```" };
        var decider = CreateDecider(TwoRouteConfig(), fake);

        var decision = await decider.DecideAsync([ImageMessage("看看")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-vision", decision.Route.Name);
        Assert.True(decision.EnableMultimodal);
    }

    [Fact]
    public async Task Image_RouterThrows_FallsBackToVision()
    {
        var fake = new FakeLlmClient { ThrowOnCall = new HttpRequestException("router down") };
        var decider = CreateDecider(TwoRouteConfig(), fake);

        var decision = await decider.DecideAsync([ImageMessage("看看这个图")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-vision", decision.Route.Name); // 失败回退：有图→视觉
        Assert.True(decision.EnableMultimodal);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task RouterPrompt_ContainsImageSample_NotFullBase64()
    {
        var fake = new FakeLlmClient { ResponseContent = """{"multimodal": false}""" };
        var decider = CreateDecider(TwoRouteConfig(), fake);

        await decider.DecideAsync([ImageMessage("今天天气如何")], options: null, streaming: false, CancellationToken.None);

        var prompt = fake.LastMessages!.Single().Content;
        Assert.Contains("今天天气如何", prompt);
        Assert.Contains("[image:base64: ", prompt);      // 采样信号在
        Assert.DoesNotContain("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", prompt); // 完整 base64 不上线
        Assert.Contains("multimodal", prompt);
    }

    [Fact]
    public async Task RouterPrompt_WithoutImage_ShowsNone()
    {
        var fake = new FakeLlmClient { ResponseContent = """{"multimodal": false}""" };
        var config = TwoRouteConfig();
        // 强制走路由：模拟"有图但采集为空"不可行——无图时直接短路。
        // 改为直接验证 prompt 构造函数的 none 分支。
        var prompt = LlmRouteDecider.BuildRouterPrompt("hi", []);
        Assert.Contains("Context images: (none)", prompt);
    }

    [Fact]
    public async Task Image_RouterSaysNo_WithCallerMultimodalEnabled_PicksTextRoute()
    {
        // 调用方（决策模型工厂）因上下文有图设 EnableMultimodal=true → 原候选只剩视觉模型。
        // 路由否决识图后必须重算候选，让文本模型可入选（回归：此前会回退到视觉模型）。
        var fake = new FakeLlmClient { ResponseContent = """{"multimodal": false}""" };
        var decider = CreateDecider(TwoRouteConfig(), fake);
        var options = new LlmOptions { EnableMultimodal = true };

        var decision = await decider.DecideAsync([ImageMessage("今天天气如何")], options, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-text", decision.Route.Name);
        Assert.False(decision.EnableMultimodal);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Image_RouterSaysYes_NoVisionCandidate_FallsBackToTextCandidate()
    {
        var config = TwoRouteConfig();
        config.Routes[1].Enabled = false;
        config.Routes[2].Enabled = false; // 只有文本
        var fake = new FakeLlmClient { ResponseContent = """{"multimodal": true}""" };
        var decider = CreateDecider(config, fake);

        var decision = await decider.DecideAsync([ImageMessage("看看这个图")], options: null, streaming: false, CancellationToken.None);

        Assert.Equal("deepseek-fast-text", decision.Route.Name);
        Assert.False(decision.EnableMultimodal); // 候选唯一：无视觉可用
    }
}
