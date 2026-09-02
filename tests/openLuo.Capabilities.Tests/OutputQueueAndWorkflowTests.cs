using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using Xunit;

namespace openLuo.Capabilities.Tests;

public class InMemoryOutputQueueTests
{
    [Fact]
    public async Task Enqueue_AssignsMonotonicSequences()
    {
        var queue = new InMemoryOutputQueue();
        var seq1 = await queue.EnqueueAsync(new OutputItem { Id = "a", Kind = ReplyItemKind.Text, Payload = "x" });
        var seq2 = await queue.EnqueueAsync(new OutputItem { Id = "b", Kind = ReplyItemKind.Text, Payload = "y" });

        Assert.True(seq1 < seq2);
    }

    [Fact]
    public async Task Read_YieldsInOrder()
    {
        var queue = new InMemoryOutputQueue();
        await queue.EnqueueAsync(new OutputItem { Id = "a", Kind = ReplyItemKind.Text, Payload = "x" });
        await queue.EnqueueAsync(new OutputItem { Id = "b", Kind = ReplyItemKind.Text, Payload = "y" });

        var items = new List<OutputItem>();
        await foreach (var item in queue.ReadAsync().Take(2))
            items.Add(item);

        Assert.Equal(2, items.Count);
        Assert.Equal("a", items[0].Id);
        Assert.Equal("b", items[1].Id);
    }

    [Fact]
    public async Task Fail_Permanent_RemovesItem()
    {
        var queue = new InMemoryOutputQueue();
        var seq = await queue.EnqueueAsync(new OutputItem { Id = "a", Kind = ReplyItemKind.Text, Payload = "x" });
        await queue.FailAsync(seq, permanent: true);

        // permanent 失败项不应被产出；队列无其他项时读取应无产出
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var seen = new List<OutputItem>();
        try
        {
            await foreach (var item in queue.ReadAsync(cts.Token))
                seen.Add(item);
        }
        catch (OperationCanceledException)
        {
            // 超时 = 无产出，符合预期
        }

        Assert.Empty(seen);
    }

    [Fact]
    public async Task Fail_NonPermanent_KeepsItemRetryable()
    {
        var queue = new InMemoryOutputQueue();
        var seq = await queue.EnqueueAsync(new OutputItem { Id = "a", Kind = ReplyItemKind.Text, Payload = "x" });
        await queue.FailAsync(seq, permanent: false);

        // 可重试失败项仍可被消费（状态 RetryableFailure 仍产出）
        var items = new List<OutputItem>();
        await foreach (var item in queue.ReadAsync().Take(1))
            items.Add(item);

        Assert.Single(items);
    }
}

public class IdempotencyKeyTests
{
    [Fact]
    public void Create_SameInputs_ProducesStableKey()
    {
        var options = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };
        var key1 = IdempotencyKeys.Create("world:test", "d1", ["x"], options);
        var key2 = IdempotencyKeys.Create("world:test", "d1", ["x"], new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Create_DifferentInputs_ProducesDifferentKeys()
    {
        var key1 = IdempotencyKeys.Create("world:test", "d1", ["x"], new Dictionary<string, string>());
        var key2 = IdempotencyKeys.Create("world:test", "d1", ["y"], new Dictionary<string, string>());

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Registry_RecordsAndRetrieves()
    {
        var registry = new InMemoryIdempotencyRegistry();
        var result = new CapabilityResult { InvocationId = "i1", Success = true, Status = CapabilityStatus.Ok, Text = "ok" };
        registry.Record("key-1", result);

        Assert.Same(result, registry.TryGet("key-1"));
        Assert.Null(registry.TryGet("key-2"));
    }
}

public class DefaultWorkflowRunnerTests
{
    private sealed class EchoHandler : IWorkflowNodeHandler
    {
        public string HandlerId => "echo";
        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNode node, WorkflowRunRequest request, IReadOnlyDictionary<string, object?> state, CancellationToken ct = default) =>
            Task.FromResult(new WorkflowNodeResult { Success = true, Outputs = new Dictionary<string, object?> { ["echoed"] = state.GetValueOrDefault("input") } });
    }

    [Fact]
    public async Task Run_LinearFlow_Completes()
    {
        var runner = new DefaultWorkflowRunner([new EchoHandler()]);
        runner.Register(new WorkflowDefinition
        {
            Id = "test:flow",
            StartNodeId = "start",
            Nodes =
            [
                new WorkflowNode { Id = "start", Kind = "step", HandlerId = "echo" },
                new WorkflowNode { Id = "done", Kind = "terminal" }
            ],
            Edges = [new WorkflowEdge { FromNodeId = "start", ToNodeId = "done" }]
        });

        var result = await runner.RunAsync(new WorkflowRunRequest
        {
            WorkflowId = "test:flow",
            SubjectId = "s1",
            Input = new Dictionary<string, object?> { ["input"] = "hello" }
        });

        Assert.True(result.Success);
        Assert.Equal("done", result.TerminalNodeId);
        Assert.Equal("hello", result.Outputs["input"]);
    }

    [Fact]
    public async Task Run_UnknownWorkflow_Fails()
    {
        var runner = new DefaultWorkflowRunner([]);
        var result = await runner.RunAsync(new WorkflowRunRequest { WorkflowId = "nope", SubjectId = "s1" });

        Assert.False(result.Success);
        Assert.Contains("unknown workflow", result.Error);
    }
}
