using openLuo.Modules.Executor.Application.GiftIntent;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Executor.Tests;

public sealed class GiftIntentPromptBuilderTests
{
    [Fact]
    public void Build_IncludesCharacterInputAndInventory()
    {
        var builder = new GiftIntentPromptBuilder();

        var prompt = builder.Build(new GiftIntentInput
        {
            TargetCharacterName = "汐泠",
            PlayerInput = "这枚铃铛送给你。",
            InventoryItems =
            [
                new GiftIntentInventoryItem
                {
                    Id = "bell",
                    Name = "银铃",
                    Description = "清脆的小铃铛",
                    Quantity = 1
                }
            ]
        });

        Assert.Single(prompt.Messages);
        Assert.Equal(ChatMessageRole.User, prompt.Messages[0].Role);
        Assert.Contains("汐泠", prompt.Messages[0].Content);
        Assert.Contains("这枚铃铛送给你。", prompt.Messages[0].Content);
        Assert.Contains("银铃", prompt.Messages[0].Content);
        // Temperature and MaxTokens are controlled by ExecutorConfigs, not hardcoded defaults
    }
}

public sealed class GiftIntentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsGiftIntentOutput()
    {
        var executor = new GiftIntentExecutor(
            new StubLlmClient(
                """
                {
                  "hasGiftIntent": true,
                  "itemRef": "银铃",
                  "confidence": 0.91,
                  "reason": "玩家明确说送给你"
                }
                """),
            new GiftIntentPromptBuilder(),
            new StructuredOutputParser());

        var result = await executor.ExecuteAsync(new GiftIntentInput
        {
            TargetCharacterName = "汐泠",
            PlayerInput = "银铃送给你。",
            InventoryItems = [new GiftIntentInventoryItem { Id = "bell", Name = "银铃", Quantity = 1 }]
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.True(result.Output!.HasGiftIntent);
        Assert.Equal("银铃", result.Output.ItemRef);
        Assert.Equal(0.91, result.Output.Confidence);
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
