using NSubstitute;
using OpenLuo.Extensions.Memory;
using openLuo.Abstractions;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using Xunit;

namespace openLuo.Extensions.Tests;

/// <summary>
/// memory 能力的"用户维度"接线（D45）：invoker 必须把 <see cref="CapabilityExecutionContext.UserId"/>
/// 填进写入输入 / 召回查询。
/// 消费端断言不可省——上下文逐调用下传如果漏接，不会报错，只会静默把 A 的记忆当成 B 的。
/// </summary>
public sealed class MemoryCapabilityUserDimensionTests
{
    [Fact]
    public async Task memory_write能力_把执行上下文UserId落进写入输入()
    {
        var write = Substitute.For<IMemoryWriteService>();
        MemoryWriteInput? captured = null;
        write.WriteAsync(Arg.Do<MemoryWriteInput>(input => captured = input), Arg.Any<CancellationToken>())
            .Returns(new MemoryWriteResult { Success = true, MemoryId = "m-1" });

        var writeInvoker = BuildInvoker("write", Substitute.For<IMemoryRecallService>(), write);
        var result = await writeInvoker.InvokeAsync(
            new CapabilityCall
            {
                InvocationId = "inv-1",
                CanonicalId = "memory:write",
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["content"] = "用户喜欢樱花" }
            },
            Context(userId: "10001"));

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("10001", captured!.UserId);
        Assert.Equal("builtin-rin", captured.CharacterId);
        Assert.Equal("session-1", captured.GameId);
    }

    [Fact]
    public async Task memory_write能力_平台没给userId时写入输入留空_即不分区()
    {
        var write = Substitute.For<IMemoryWriteService>();
        MemoryWriteInput? captured = null;
        write.WriteAsync(Arg.Do<MemoryWriteInput>(input => captured = input), Arg.Any<CancellationToken>())
            .Returns(new MemoryWriteResult { Success = true, MemoryId = "m-1" });

        var writeInvoker = BuildInvoker("write", Substitute.For<IMemoryRecallService>(), write);
        await writeInvoker.InvokeAsync(
            new CapabilityCall
            {
                InvocationId = "inv-1",
                CanonicalId = "memory:write",
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["content"] = "用户喜欢樱花" }
            },
            Context(userId: null));

        Assert.NotNull(captured);
        Assert.Null(captured!.UserId);
    }

    [Fact]
    public async Task memory_search能力_把执行上下文UserId填进召回查询()
    {
        var recall = Substitute.For<IMemoryRecallService>();
        SemanticRecallQuery? captured = null;
        recall.RecallAsync(Arg.Do<SemanticRecallQuery>(query => captured = query), Arg.Any<CancellationToken>())
            .Returns(new MemoryRecallResult { Success = true });

        var searchInvoker = BuildInvoker("search", recall, Substitute.For<IMemoryWriteService>());
        var result = await searchInvoker.InvokeAsync(
            new CapabilityCall
            {
                InvocationId = "inv-2",
                CanonicalId = "memory:search",
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["query"] = "樱花" }
            },
            Context(userId: "10001"));

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("10001", captured!.UserId);
    }

    [Fact]
    public async Task 基线贡献者_用请求UserId填召回查询_无userId时留空且内容照常产出()
    {
        var recall = Substitute.For<IMemoryRecallService>();
        var queries = new List<SemanticRecallQuery>();
        recall.RecallAsync(Arg.Do<SemanticRecallQuery>(query => queries.Add(query)), Arg.Any<CancellationToken>())
            .Returns(new MemoryRecallResult
            {
                Success = true,
                Records = [new MemoryRecord { Id = "m-1", Summary = "上次约好去看樱花" }]
            });

        var contributor = new MemoryBaselineContributor(recall);
        Assert.Equal(ContextScope.User, contributor.Scope);

        var withUser = await contributor.ContributeAsync(Request(userId: "10001"));
        Assert.Equal("10001", queries[^1].UserId);
        Assert.Single(withUser.Contributions);

        var withoutUser = await contributor.ContributeAsync(Request(userId: null));
        Assert.Null(queries[^1].UserId);
        Assert.Single(withoutUser.Contributions);
    }

    private static ContextBuildRequest Request(string? userId) => new()
    {
        SessionId = "session-1",
        SubjectId = "builtin-rin",
        ConversationId = "qq-group-1",
        UserId = userId,
        UserInput = "我们上次说到哪了"
    };

    private static CapabilityExecutionContext Context(string? userId) => new()
    {
        GameId = "session-1",
        SessionId = "session-1",
        SubjectId = "builtin-rin",
        ConversationId = "qq-group-1",
        UserId = userId,
        InvocationId = "inv"
    };

    private static ICapabilityInvoker BuildInvoker(
        string canonicalId, IMemoryRecallService recall, IMemoryWriteService write)
    {
        var builder = new ExtensionBuilder("memory");
        new MemoryExtension(recall, write).Configure(builder);
        return builder.InvokableCapabilities.Single(c => c.Descriptor.CanonicalId == canonicalId).Invoker;
    }
}
