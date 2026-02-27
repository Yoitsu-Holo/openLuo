using openLuo.Modules.Executor.Application.CharacterResponse;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Executor.Tests;

public sealed class CharacterResponsePromptBuilderTests
{
    [Fact]
    public void Build_CreatesRoleplaySystemPromptAndContextBlocks()
    {
        var builder = new CharacterResponsePromptBuilder();

        var prompt = builder.Build(new CharacterResponseInput
        {
            CharacterProfile = "名字：neko",
            WorldContext = "架空世界",
            SceneState = "宅邸门口",
            LongTermMemory = "玩家怕冷",
            ToolResults = ["已查询天气：雨天"],
            Conversation =
            [
                new LocalChatMessage(ChatMessageRole.Assistant, "先进来吧。")
            ],
            PlayerInput = "我回来了。"
        });

        Assert.Equal(ChatMessageRole.System, prompt.Messages[0].Role);
        Assert.Contains("沉浸式角色扮演", prompt.Messages[0].Content);
        Assert.Contains("[CHARACTER_PROFILE]", prompt.Messages[1].Content);
        Assert.Contains("[WORLD_CONTEXT]", prompt.Messages[2].Content);
        Assert.Contains("[SCENE_STATE]", prompt.Messages[3].Content);
        Assert.Contains("[LONG_TERM_MEMORY]", prompt.Messages[4].Content);
        Assert.Contains("tool_results:", prompt.Messages[5].Content);
        Assert.Equal(ChatMessageRole.Assistant, prompt.Messages[^2].Role);
        Assert.Equal(ChatMessageRole.User, prompt.Messages[^1].Role);
        Assert.Equal("我回来了。", prompt.Messages[^1].Content);
    }
}

public sealed class CharacterResponseExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsPlainCharacterReply()
    {
        var llm = new StubLlmClient("（轻轻递上毛巾）先擦干头发吧，别着凉。");
        var executor = new CharacterResponseExecutor(
            llm,
            new CharacterResponsePromptBuilder());

        var result = await executor.ExecuteAsync(new CharacterResponseInput
        {
            CharacterProfile = "名字：汐泠",
            PlayerInput = "我淋雨回来了。"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Contains("先擦干头发", result.Output);
    }

    private sealed class StubLlmClient : ILlmClient
    {
        private readonly string _reply;

        public StubLlmClient(string reply)
        {
            _reply = reply;
        }

        public Task<string> CompleteAsync(
            IEnumerable<LocalChatMessage> messages,
            LlmOptions? options = null,
            CancellationToken ct = default) =>
            Task.FromResult(_reply);

        public Task<string> StreamAsync(
            IEnumerable<LocalChatMessage> messages,
            Action<string> onChunk,
            LlmOptions? options = null,
            CancellationToken ct = default)
        {
            onChunk(_reply);
            return Task.FromResult(_reply);
        }
    }
}
