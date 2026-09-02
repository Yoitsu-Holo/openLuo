using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Llm;
using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Tests.Llm;

public sealed class LlmCapabilityDecisionModelTests
{
    private sealed class CapturingLlmClient : ILlmClient
    {
        public IReadOnlyList<LocalChatMessage>? LastMessages { get; private set; }
        public LlmOptions? LastOptions { get; private set; }
        public LlmChatResponse Response { get; set; } = new() { Content = "回复" };

        public Task<LlmChatResponse> CompleteAsync(IEnumerable<LocalChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            return Task.FromResult(Response);
        }

        public Task<string> StreamAsync(IEnumerable<LocalChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static CapabilityDecisionContext BuildContext(ImageBlock? historyImage, ImageBlock? userImage)
    {
        var conversation = new List<ContextMessage>();
        if (historyImage is not null)
            conversation.Add(new ContextMessage { Role = "user", Content = "[image] 历史图", Blocks = [historyImage] });
        conversation.Add(new ContextMessage { Role = "user", Content = "普通历史消息" });

        return new CapabilityDecisionContext
        {
            SessionId = "s1", TurnId = "t1", SnapshotVersion = 1,
            SystemBlocks = ["[Identity]\n角色\n[/Identity]"],
            Conversation = conversation,
            UserInput = userImage is null ? "你好" : "看看这张图",
            UserBlocks = userImage is null ? null : [userImage]
        };
    }

    [Fact]
    public async Task DecideAsync_PropagatesHistoryAndUserImageBlocks()
    {
        var client = new CapturingLlmClient();
        var model = new LlmCapabilityDecisionModel(client);
        var historyImage = new ImageBlock { Kind = BlockKind.Image, AssetId = "h1", MimeType = "image/png" };
        var userImage = new ImageBlock { Kind = BlockKind.Image, AssetId = "u1", MimeType = "image/jpeg" };

        var decision = await model.DecideAsync(BuildContext(historyImage, userImage), CancellationToken.None);

        var respond = Assert.Single(decision.Messages);
        Assert.Equal(FlowMode.Respond, respond.Mode);
        Assert.Equal("回复", respond.Payload);
        // 历史 user 消息携带其图片块
        var historyMessage = client.LastMessages!.Single(m => m.Content.Contains("历史图"));
        Assert.Same(historyImage, Assert.Single(historyMessage.Blocks!));
        // 当前输入（最后一条 user 消息）携带 UserBlocks
        var lastUser = client.LastMessages!.Last(m => m.Role == ChatMessageRole.User);
        Assert.Same(userImage, Assert.Single(lastUser.Blocks!));
        Assert.Contains("看看这张图", lastUser.Content);
    }

    [Fact]
    public async Task DecideAsync_AutoEnablesMultimodalWhenImagesPresent()
    {
        var client = new CapturingLlmClient();
        var model = new LlmCapabilityDecisionModel(client);
        var userImage = new ImageBlock { Kind = BlockKind.Image, AssetId = "u1", MimeType = "image/png" };

        await model.DecideAsync(BuildContext(historyImage: null, userImage), CancellationToken.None);

        Assert.True(client.LastOptions!.EnableMultimodal);
    }

    [Fact]
    public async Task DecideAsync_DisablesMultimodalWithoutImages()
    {
        var client = new CapturingLlmClient();
        var model = new LlmCapabilityDecisionModel(client);

        await model.DecideAsync(BuildContext(historyImage: null, userImage: null), CancellationToken.None);

        Assert.False(client.LastOptions!.EnableMultimodal);
    }

    [Fact]
    public async Task DecideAsync_TextWithToolCall_ClassifiedAsInqueue()
    {
        var client = new CapturingLlmClient();
        client.Response = new LlmChatResponse
        {
            Content = "你是想让我发张图来看看吗？我这边没有现成的图可以发",
            ToolCalls = [new LlmToolCall { Id = "c1", Name = "mcp_media_fetch_random_image", ArgumentsJson = "{}" }]
        };
        var model = new LlmCapabilityDecisionModel(client);

        var decision = await model.DecideAsync(BuildContext(historyImage: null, userImage: null), CancellationToken.None);

        var message = Assert.Single(decision.Messages);
        Assert.Equal(FlowMode.Inqueue, message.Mode);
        Assert.Contains("你是想让我发张图", Convert.ToString(message.Payload));
        var call = Assert.Single(decision.Calls);
        Assert.Equal("mcp_media_fetch_random_image", call.ModelToolName);
    }
}
