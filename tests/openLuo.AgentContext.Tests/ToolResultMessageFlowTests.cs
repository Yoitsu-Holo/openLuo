using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core.Models;
using Xunit;

namespace openLuo.AgentContext.Tests;

public sealed class ToolResultMessageFlowTests
{
    private sealed class MemoryStore : IConversationStore
    {
        private readonly List<ConversationTurn> _turns = [];
        public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default)
        {
            var result = _turns.Where(t => t.SessionId == sessionId).TakeLast(limit).ToList();
            return Task.FromResult<IReadOnlyList<ConversationTurn>>(result);
        }
        public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default)
        {
            _turns.Add(turn);
            return Task.CompletedTask;
        }
    }

    private static DefaultAgentContextSession CreateSession()
    {
        var assembler = new DefaultContextAssembler([]);
        var session = new DefaultAgentContextSession("s", "subject", assembler, new MemoryStore(), new DefaultMessageTagPipeline());
        return session;
    }

    [Fact]
    public async Task ApplyToolResults_AppendsAssistantToolCallsThenToolMessage()
    {
        var session = CreateSession();
        await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "t1", UserInput = "来张图看看"
        }, CancellationToken.None);

        var calls = new List<CapabilityCall>
        {
            new()
            {
                InvocationId = "inv-1", ModelCallId = "call_00_abc", ModelToolName = "cap_media_fetch_x",
                CanonicalId = "media:fetch_random_image", RawArgumentsJson = "{\"args\":[],\"options\":{}}"
            }
        };
        var results = new List<CapabilityResult>
        {
            new() { InvocationId = "inv-1", Success = false, Text = null, Error = "media source did not return an image" }
        };

        await session.ApplyToolResultsAsync(calls, results, CancellationToken.None);

        var conversation = session.Current.Conversation;
        Assert.Equal(2, conversation.Count);
        Assert.Equal("assistant", conversation[0].SpeakerRole);
        Assert.Contains("call_00_abc", conversation[0].ToolCallsJson);
        Assert.Contains("cap_media_fetch_x", conversation[0].ToolCallsJson);
        Assert.Equal("tool", conversation[1].SpeakerRole);
        Assert.Equal("call_00_abc", conversation[1].ToolCallId);
        Assert.Equal("media source did not return an image", conversation[1].Content);
    }

    [Fact]
    public async Task ApplyToolResults_SkipsCallsWithoutModelCallId()
    {
        var session = CreateSession();
        await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "s", SubjectId = "subject", TurnId = "t1", UserInput = "hi"
        }, CancellationToken.None);

        var calls = new List<CapabilityCall>
        {
            new() { InvocationId = "inv-1", CanonicalId = "media:fetch_random_image" }   // 无 ModelCallId
        };
        var results = new List<CapabilityResult>
        {
            new() { InvocationId = "inv-1", Success = false, Error = "err" }
        };

        await session.ApplyToolResultsAsync(calls, results, CancellationToken.None);

        Assert.Empty(session.Current.Conversation);   // 无模型 id → 不追加任何消息
    }
}
