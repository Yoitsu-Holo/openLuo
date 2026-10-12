using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using openLuo.Infrastructure.Persistence;
using Xunit;

namespace openLuo.Tests.Composition;

/// <summary>
/// 世界状态的持久化：提交后的状态（版本 + 值）落 <c>hub.db</c>，重启（新建事务实例）后仍能读回，
/// 于是 `GET /v1/sessions/{id}/state` 与 `state.updated` 的版本不会因重启而归零。
/// 见 docs/architecture/hub-protocol.md §5.4。
/// </summary>
public sealed class StatePersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "openluo-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task 提交后落盘_重启仍能读回版本与值()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "hub.db");

        var first = new InMemoryStateTransaction(new HubStore(dbPath));
        var result = await first.CommitAsync("builtin-rin", 0,
        [
            new MutationIntent { SubjectId = "builtin-rin", ResourcePath = "mood", Op = MutationOp.Set, Value = "happy" },
        ]);
        Assert.Equal(MutationBatchStatus.Committed, result.Status);

        // 模拟重启：全新的事务实例，仅凭同一个 hub.db
        var afterRestart = new InMemoryStateTransaction(new HubStore(dbPath));
        var view = afterRestart.TryGet("builtin-rin");

        Assert.NotNull(view);
        Assert.Equal(1, view!.Version);
        // 值按 JSON 往返：回载后是 JSON 节点（Hub 投影到协议时本就序列化为 JSON）
        Assert.Equal("happy", Assert.IsAssignableFrom<JsonNode>(view.Values["mood"]).GetValue<string>());
    }

    [Fact]
    public async Task 未接入持久化时行为不变()
    {
        var transaction = new InMemoryStateTransaction();
        var result = await transaction.CommitAsync("s", 0,
            [new MutationIntent { SubjectId = "s", ResourcePath = "k", Op = MutationOp.Set, Value = 1 }]);

        Assert.Equal(MutationBatchStatus.Committed, result.Status);
        Assert.Equal(1, transaction.TryGet("s")!.Version);
    }
}
