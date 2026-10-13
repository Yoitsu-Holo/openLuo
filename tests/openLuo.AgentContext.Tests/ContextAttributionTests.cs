using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using Xunit;

namespace openLuo.AgentContext.Tests;

/// <summary>
/// 历史归因（D45）：`speaker_id` 必须是**稳定身份**，显示名只用于渲染。
/// 改造前这里写的是显示名——群聊里改名/同名即串味，且没法按人分区。
/// </summary>
public sealed class ContextAttributionTests
{
    private sealed class RecordingStore : IConversationStore
    {
        public List<ConversationTurn> Turns { get; } = [];

        public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationTurn>>(Turns.TakeLast(limit).ToList());

        public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default)
        {
            Turns.Add(turn);
            return Task.CompletedTask;
        }
    }

    private static DefaultAgentContextSession Session(RecordingStore store, string conversationId = "qq-group-1") =>
        new("sess_1", "builtin-rin", new DefaultContextAssembler([]), store, new DefaultMessageTagPipeline(),
            conversationId: conversationId);

    [Fact]
    public async Task Commit_WritesStableSpeakerIdAndKeepsDisplayNameSeparate()
    {
        var store = new RecordingStore();
        var session = Session(store);

        await session.CommitTurnAsync(new TurnCompletion
        {
            TurnId = "turn_1", UserText = "你好呀", SenderName = "阿明", UserId = "10001",
            FinalText = "你好！", ThreadId = "topic-7",
        });

        var inbound = Assert.Single(store.Turns, t => t.SpeakerRole == "inbound");
        Assert.Equal("10001", inbound.SpeakerId);          // 稳定身份
        Assert.Equal("阿明", inbound.SpeakerName);         // 显示名单独保留
        Assert.Equal("topic-7", inbound.ThreadId);
        Assert.Equal("turn_1:user", inbound.TurnId);

        var outbound = Assert.Single(store.Turns, t => t.SpeakerRole == "outbound");
        Assert.Equal("builtin-rin", outbound.SpeakerId);   // 角色的发言归到角色
        Assert.Equal("turn_1:assistant", outbound.TurnId);
    }

    [Fact]
    public async Task Commit_WithoutUserId_FallsBackToSubject()
    {
        // 平台没给稳定身份（老客户端/未实现 userId 的通道）→ 行为与改造前一致
        var store = new RecordingStore();
        var session = Session(store);

        await session.CommitTurnAsync(new TurnCompletion
        {
            TurnId = "turn_2", UserText = "无身份消息", SenderName = "阿明", FinalText = "收到",
        });

        var inbound = Assert.Single(store.Turns, t => t.SpeakerRole == "inbound");
        Assert.Equal("builtin-rin", inbound.SpeakerId);
        Assert.Equal("阿明", inbound.SpeakerName);
        Assert.Null(inbound.ThreadId);
    }

    [Fact]
    public async Task TwoUsersInOneConversation_GetDistinctSpeakerIds()
    {
        // 群聊场景：同一段对话里两个人发言，历史必须能区分（旧实现两条都是显示名，同名即混淆）
        var store = new RecordingStore();
        var session = Session(store);

        await session.CommitTurnAsync(new TurnCompletion
        { TurnId = "t1", UserText = "我是阿明", SenderName = "阿明", UserId = "10001", FinalText = "嗯" });
        await session.CommitTurnAsync(new TurnCompletion
        { TurnId = "t2", UserText = "我也是阿明", SenderName = "阿明", UserId = "10002", FinalText = "哦" });

        var speakers = store.Turns.Where(t => t.SpeakerRole == "inbound").Select(t => t.SpeakerId).ToList();
        Assert.Equal(["10001", "10002"], speakers);   // 同名不同人 → 身份仍然可分
    }

    [Fact]
    public async Task Snapshot_CarriesDomainIdentityForContributors()
    {
        // 贡献者要能按域取键：请求里的身份必须一路传进快照构建（供记忆按 User 域分区）
        var seen = new List<ContextBuildRequest>();
        var assembler = new CapturingAssembler(seen);
        var session = new DefaultAgentContextSession(
            "sess_1", "builtin-rin", assembler, new RecordingStore(), new DefaultMessageTagPipeline(),
            conversationId: "qq-group-1");

        await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = "sess_1", SubjectId = "builtin-rin", TurnId = "t1",
            ConversationId = "qq-group-1", UserId = "10001", ThreadId = "topic-7",
        });

        var request = Assert.Single(seen);
        Assert.Equal("builtin-rin@10001", request.DomainKey(ContextScope.User)!.Key);
        Assert.Equal("qq-group-1#topic-7", request.DomainKey(ContextScope.Thread)!.Key);
    }

    private sealed class CapturingAssembler(List<ContextBuildRequest> sink) : IContextAssembler
    {
        public Task<AgentDecisionContext> BuildAsync(ContextBuildRequest request, CancellationToken ct = default)
        {
            sink.Add(request);
            return Task.FromResult(new AgentDecisionContext
            {
                SessionId = request.SessionId, TurnId = request.TurnId,
            });
        }
    }
}
