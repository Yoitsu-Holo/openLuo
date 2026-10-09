using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using Xunit;

namespace openLuo.Capabilities.Tests;

/// <summary>输出队列解耦：Enqueue 永不阻塞（无消费者也不卡内核）；ReadSince 支持断线续传。</summary>
public class OutputQueueDecouplingTests
{
    private static OutputItem Item(string id, string? conversationId = null) =>
        new() { Id = id, Kind = ReplyItemKind.Text, Payload = id, ConversationId = conversationId };

    [Fact]
    public async Task Enqueue_WithoutConsumer_DoesNotBlock()
    {
        var queue = new InMemoryOutputQueue(maxRetainedPerSession: 16);

        // 远超保留窗口：旧实现（有界 1024 + FullMode.Wait）在无消费者时会阻塞在此。
        var enqueue = Task.Run(async () =>
        {
            for (var i = 0; i < 5000; i++)
                await queue.EnqueueAsync(Item($"m{i}", "conv"));
        });

        var completed = await Task.WhenAny(enqueue, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(enqueue, completed);
        await enqueue;
    }

    [Fact]
    public async Task Read_YieldsAllPending_InOrder_WithMonotonicSequence()
    {
        var queue = new InMemoryOutputQueue();
        for (var i = 0; i < 5; i++)
            await queue.EnqueueAsync(Item($"m{i}"));

        var items = new List<OutputItem>();
        await foreach (var it in queue.ReadAsync().Take(5))
            items.Add(it);

        Assert.Equal(["m0", "m1", "m2", "m3", "m4"], items.Select(i => i.Id).ToArray());
        Assert.True(items[0].Sequence < items[^1].Sequence);
    }

    [Fact]
    public async Task ReadSince_ExcludesAcked_AndReturnsSubsequent()
    {
        var queue = new InMemoryOutputQueue();
        var s1 = await queue.EnqueueAsync(Item("m1", "conv"));
        await queue.EnqueueAsync(Item("m2", "conv"));
        await queue.EnqueueAsync(Item("m3", "conv"));

        await queue.AckAsync(s1);

        Assert.Equal(["m2", "m3"], queue.ReadSince("conv", 0).Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task ReadSince_ReturnsItemsReadButNotAcked()
    {
        var queue = new InMemoryOutputQueue();
        await queue.EnqueueAsync(Item("m1", "conv"));
        await queue.EnqueueAsync(Item("m2", "conv"));

        // 消费者取走（推进游标）但未 Ack → 续传仍应补发（未确认投递）。
        await foreach (var _ in queue.ReadAsync().Take(2)) { }

        Assert.Equal(2, queue.ReadSince("conv", 0).Count);
    }

    [Fact]
    public async Task ReadSince_HonorsSinceSequence()
    {
        var queue = new InMemoryOutputQueue();
        var s1 = await queue.EnqueueAsync(Item("m1", "conv"));
        var s2 = await queue.EnqueueAsync(Item("m2", "conv"));
        await queue.EnqueueAsync(Item("m3", "conv"));

        Assert.True(s2 > s1);
        Assert.Equal(["m2", "m3"], queue.ReadSince("conv", s1).Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task ReadSince_ExcludesPermanentFailure()
    {
        var queue = new InMemoryOutputQueue();
        var s1 = await queue.EnqueueAsync(Item("m1", "conv"));
        await queue.EnqueueAsync(Item("m2", "conv"));

        await queue.FailAsync(s1, permanent: true);

        Assert.Equal(["m2"], queue.ReadSince("conv", 0).Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task ReadSince_ScopedToConversation()
    {
        var queue = new InMemoryOutputQueue();
        await queue.EnqueueAsync(Item("a", "conv-a"));
        await queue.EnqueueAsync(Item("b", "conv-b"));

        Assert.Equal(["a"], queue.ReadSince("conv-a", 0).Select(i => i.Id).ToArray());
        Assert.Equal(2, queue.ReadSince(null, 0).Count);
    }

    [Fact]
    public async Task Enqueue_EvictsOldestBeyondWindow()
    {
        var queue = new InMemoryOutputQueue(maxRetainedPerSession: 16);
        for (var i = 0; i < 100; i++)
            await queue.EnqueueAsync(Item($"m{i}", "conv"));

        var pending = queue.ReadSince("conv", 0);
        Assert.True(pending.Count <= 16);
        Assert.DoesNotContain("m0", pending.Select(i => i.Id));
    }

    [Fact]
    public async Task Enqueue_Concurrent_AssignsUniqueSequences()
    {
        var queue = new InMemoryOutputQueue(maxRetainedPerSession: 8192);

        var writers = Enumerable.Range(0, 8).Select(t => Task.Run(async () =>
        {
            for (var i = 0; i < 500; i++)
                await queue.EnqueueAsync(Item($"t{t}-{i}", $"conv-{t % 3}"));
        })).ToArray();

        await Task.WhenAll(writers);

        var all = queue.ReadSince(null, 0);
        Assert.Equal(4000, all.Count);
        // 序号全局唯一：跨会话并行 Enqueue 若有竞态会出现重复/跳号。
        Assert.Equal(4000, all.Select(i => i.Sequence).Distinct().Count());
    }
}
