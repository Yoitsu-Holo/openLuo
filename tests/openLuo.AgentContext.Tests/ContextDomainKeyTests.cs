using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using Xunit;

namespace openLuo.AgentContext.Tests;

/// <summary>
/// 上下文域键（D45）。键形状是"按域存取"的公共契约，改形状必须改这里——
/// 记忆/历史/在成都按它寻址，形状漂移会造成静默串域。
/// </summary>
public sealed class ContextDomainKeyTests
{
    private static ContextBuildRequest Request(
        string sessionId = "sess_1",
        string subjectId = "builtin-rin",
        string conversationId = "",
        string? userId = null,
        string? threadId = null) => new()
    {
        SessionId = sessionId,
        SubjectId = subjectId,
        ConversationId = conversationId,
        UserId = userId,
        ThreadId = threadId,
    };

    [Fact]
    public void Global_IsKeyedBySubject()
    {
        var key = Request().DomainKey(ContextScope.Global);

        Assert.NotNull(key);
        Assert.Equal(new ContextDomainKey(ContextScope.Global, "builtin-rin"), key);
    }

    [Fact]
    public void Session_PrefersConversationIdAndFallsBackToSessionId()
    {
        Assert.Equal("qq-group-1", Request(conversationId: "qq-group-1").DomainKey(ContextScope.Session)!.Key);
        // 未给会话键（老客户端/单机）时回退到 sessionId，保持改造前语义
        Assert.Equal("sess_1", Request().DomainKey(ContextScope.Session)!.Key);
    }

    [Fact]
    public void Thread_NeedsThreadId()
    {
        Assert.Null(Request().DomainKey(ContextScope.Thread));
        Assert.Equal("qq-group-1#topic-7",
            Request(conversationId: "qq-group-1", threadId: "topic-7").DomainKey(ContextScope.Thread)!.Key);
    }

    [Fact]
    public void User_NeedsUserIdAndIsCrossSession()
    {
        Assert.Null(Request().DomainKey(ContextScope.User));

        // 关键语义：User 域**不含会话**——同一个人在不同群/不同端是同一份关系记忆
        var inGroup = Request(conversationId: "qq-group-1", userId: "10001").DomainKey(ContextScope.User);
        var inPrivate = Request(conversationId: "qq-private-10001", userId: "10001").DomainKey(ContextScope.User);

        Assert.Equal(inGroup, inPrivate);
        Assert.Equal("builtin-rin@10001", inGroup!.Key);
    }

    [Fact]
    public void Keys_RoundTripThroughToString()
    {
        Assert.Equal("User:builtin-rin@10001",
            ContextDomainKeys.User("builtin-rin", "10001").ToString());
        Assert.Equal("Session:qq-group-1",
            ContextDomainKeys.Session("qq-group-1").ToString());
    }

    [Fact]
    public async Task ContributorScope_DefaultsToSession()
    {
        // 既有扩展不该被迫表态：不声明 Scope 的实现按 Session 处理（改造前语义）。
        // 注意：`IContextContributor.Scope` 是接口**默认成员** → 只能经由接口类型访问。
        var legacy = new LegacyContributor();
        var assembler = new openLuo.AgentContext.Infrastructure.DefaultContextAssembler([legacy]);

        var context = await assembler.BuildAsync(Request(conversationId: "qq-group-1"));

        Assert.Equal(ContextScope.Session, ((IContextContributor)legacy).Scope);
        Assert.Single(context.Contributions);
    }

    /// <summary>模拟"改造前写的"扩展：只实现 Id + ContributeAsync。</summary>
    private sealed class LegacyContributor : IContextContributor
    {
        public string Id => "legacy";

        public Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ContextContributionResult
            {
                Contributions = [new ContextContribution { Id = "legacy:1", ContributorId = Id, Content = "x" }],
            });
    }
}
