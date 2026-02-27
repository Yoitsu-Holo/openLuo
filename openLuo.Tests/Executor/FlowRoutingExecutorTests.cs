using openLuo.Modules.Executor.Application.FlowRouting;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using LocalChatMessage = openLuo.Modules.Llm.Core.Models.ChatMessage;

namespace openLuo.Executor.Tests;

public sealed class FlowRoutingPromptBuilderTests
{
    [Fact]
    public void Build_IncludesFlowStateAndCandidates()
    {
        var builder = new FlowRoutingPromptBuilder();

        var prompt = builder.Build(new FlowRoutingInput
        {
            FlowId = "character.chat",
            CurrentNodeId = "plan",
            PreviousNodeOutput = """{"needTool":true}""",
            FlowStateSummary = "玩家正在赠送礼物",
            Candidates =
            [
                new FlowRoutingCandidate
                {
                    EdgeId = "plan-to-tool",
                    ToNodeId = "toolUse",
                    When = "计划要求调用工具时进入工具调用",
                    Priority = 10
                }
            ]
        });

        Assert.Equal(2, prompt.Messages.Count);
        Assert.Equal(ChatMessageRole.System, prompt.Messages[0].Role);
        Assert.Equal(ChatMessageRole.User, prompt.Messages[1].Role);
        Assert.Contains("character.chat", prompt.Messages[1].Content);
        Assert.Contains("plan-to-tool", prompt.Messages[1].Content);
        Assert.Contains("计划要求调用工具", prompt.Messages[1].Content);
        // Temperature and MaxTokens are controlled by ExecutorConfigs, not hardcoded defaults
    }
}

public sealed class FlowRoutingExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSelectedEdge()
    {
        var executor = new FlowRoutingExecutor(
            new StubLlmClient(
                """
                {
                  "selectedEdgeId": "plan-to-tool",
                  "selectedNodeId": "toolUse",
                  "confidence": 0.87,
                  "reason": "计划要求调用工具",
                  "stopReason": ""
                }
                """),
            new FlowRoutingPromptBuilder(),
            new StructuredOutputParser());

        var result = await executor.ExecuteAsync(new FlowRoutingInput
        {
            FlowId = "character.chat",
            CurrentNodeId = "plan",
            Candidates =
            [
                new FlowRoutingCandidate
                {
                    EdgeId = "plan-to-tool",
                    ToNodeId = "toolUse",
                    When = "需要工具时进入工具节点"
                }
            ]
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Equal("plan-to-tool", result.Output!.SelectedEdgeId);
        Assert.Equal("toolUse", result.Output.SelectedNodeId);
        Assert.Equal(0.87, result.Output.Confidence);
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
