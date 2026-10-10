using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;
using openLuo.Infrastructure.Persistence;
using Xunit;

namespace openLuo.Persistence.Tests;

/// <summary>Hub 控制面持久化：会话/作业/调度/令牌的落盘与保守恢复语义（决策 #3）。</summary>
public sealed class HubStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hubstore-tests-" + Guid.NewGuid().ToString("N"));

    public HubStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private HubStore Create() => new(Path.Combine(_dir, "hub.db"));

    [Fact]
    public void SessionStore_RestoresFromHubStore()
    {
        var store = Create();

        var first = new openLuo.Composition.SessionStore(store);
        first.SetMeta(new AgentSession
        {
            SessionId = "sess_restore", SubjectId = "u1", AgentId = "companion", ConversationId = "conv1",
        }, "cli", "c1");

        // 模拟重启：新 SessionStore 从同一 hub.db 恢复
        var restored = new openLuo.Composition.SessionStore(Create());
        var meta = restored.GetMeta("sess_restore");
        Assert.NotNull(meta);
        Assert.Equal("companion", meta!.AgentId);
        Assert.Single(restored.ListMeta());

        restored.Remove("sess_restore");
        Assert.Empty(new openLuo.Composition.SessionStore(Create()).ListMeta());
    }

    [Fact]
    public void Sessions_UpsertLoadDelete()
    {
        var store = Create();
        store.UpsertSession(new AgentSession { SessionId = "s1", SubjectId = "u1", AgentId = "companion", ConversationId = "conv1" }, "cli", "c1", DateTimeOffset.UtcNow);

        var loaded = store.LoadSessions();
        Assert.Single(loaded);
        Assert.Equal("s1", loaded[0].Session.SessionId);
        Assert.Equal("cli", loaded[0].ClientType);

        store.DeleteSession("s1");
        Assert.Empty(Create().LoadSessions());
    }

    [Fact]
    public void Jobs_PersistAndRestartMarksRunningAsFailed()
    {
        var store = Create();
        store.UpsertJob(new JobInfo("j1", "demo", JobStatusNames.Running, 0.5, "half", null, DateTimeOffset.UtcNow, null));
        store.UpsertJob(new JobInfo("j2", "demo", JobStatusNames.Succeeded, 1, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        Assert.Equal(2, Create().LoadJobs().Count);

        Assert.Equal(1, store.MarkRunningJobsFailed("interrupted"));

        var jobs = Create().LoadJobs().ToDictionary(j => j.Id);
        Assert.Equal(JobStatusNames.Failed, jobs["j1"].Status);
        Assert.Equal("interrupted", jobs["j1"].Message);
        Assert.Equal(JobStatusNames.Succeeded, jobs["j2"].Status);   // 已完成的不受影响
    }

    [Fact]
    public void Schedules_RoundTripWithPayload_AndDelete()
    {
        var store = Create();
        var at = DateTimeOffset.UtcNow.AddMinutes(10);
        store.UpsertSchedule(new ScheduleInfo("sch1", "s1", "ping", at, null, true, at, JsonNode.Parse("""{"n":1}""")));

        var loaded = Assert.Single(Create().LoadSchedules());
        Assert.Equal("sch1", loaded.Id);
        Assert.True(loaded.Enabled);
        Assert.Equal(1, loaded.Payload!["n"]!.GetValue<int>());

        store.DeleteSchedule("sch1");
        Assert.Empty(Create().LoadSchedules());
    }

    [Fact]
    public void Traces_RecordSequenceAndResult()
    {
        var store = Create();
        store.StartTurn("t1", "s1", "client", DateTimeOffset.UtcNow);
        store.AddEvent("t1", "decision", """{"step":1}""");
        store.AddEvent("t1", "turn.final", null);
        store.CompleteTurn("t1", true, "FinalReply", "done");

        var trace = Create().Get("t1");
        Assert.NotNull(trace);
        Assert.Equal("s1", trace!.SessionId);
        Assert.Equal("client", trace.Origin);
        Assert.Equal(2, trace.Events.Count);
        Assert.Equal(1, trace.Events[0].Seq);
        Assert.Equal("decision", trace.Events[0].Type);
        Assert.Equal(2, trace.Events[1].Seq);
        Assert.True(trace.Success);
        Assert.Equal("FinalReply", trace.TerminationReason);
        Assert.Equal("done", trace.FinalText);
        Assert.NotNull(trace.EndedAt);
    }

    [Fact]
    public void Traces_UnknownTurn_ReturnsNull() => Assert.Null(Create().Get("turn_missing"));

    [Fact]
    public void Tokens_SaveLoadFilterExpired_Remove()
    {
        var store = Create();
        store.Save(new openLuo.Core.Interfaces.TokenRecord("t-live", "c1", "admin", DateTimeOffset.UtcNow.AddHours(1)));
        store.Save(new openLuo.Core.Interfaces.TokenRecord("t-dead", "c2", "user", DateTimeOffset.UtcNow.AddMinutes(-1)));

        var loaded = Create().LoadAll();
        Assert.Single(loaded);
        Assert.Equal("t-live", loaded[0].Token);

        store.Remove("t-live");
        Assert.Empty(Create().LoadAll());
    }
}
