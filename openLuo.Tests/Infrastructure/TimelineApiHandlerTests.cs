using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Infrastructure.Runtime;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Infrastructure.Database;
using openLuo.Modules.GameBridge.Infrastructure.Handlers;
using openLuo.Modules.WorldState.Infrastructure.Timeline;

namespace openLuo.Infrastructure.Tests;

public class TimelineApiHandlerTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly string _connStr;

    private readonly IGameStateRepository _stateRepo = Substitute.For<IGameStateRepository>();
    private readonly ITimeService _timeService = Substitute.For<ITimeService>();
    private readonly IGameBridgeContextAccessor _bridgeContextAccessor = Substitute.For<IGameBridgeContextAccessor>();
    private readonly IGameContextAccessor _gameContextAccessor = Substitute.For<IGameContextAccessor>();

    public TimelineApiHandlerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gimai_timeline_{Guid.NewGuid():N}.db");
        _connStr = $"Data Source={_dbPath}";
    }

    public async Task InitializeAsync()
    {
        await new DatabaseInitializer(_connStr).InitializeAsync();
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        _bridgeContextAccessor.Current.Returns(new GameBridgeRequestContext { GameId = "g1" });
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreatePollAck_WorksEndToEnd()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };

        _timeService.GetSnapshotAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 500,
            TimeStr = "08:20",
            Mode = TimeMode.Virtual,
            EpochMs = 5_000
        });

        var timelineService = new TimelineService(_connStr);
        var handler = new TimelineApiHandler(_stateRepo, timelineService, _timeService, _bridgeContextAccessor);

        var createResult = await handler.CreateTimelineEventAsync("g1", "date_invite", dueAtEpochMs: 1000, title: "周六约会", action: JsonNode.Parse("""{"lockCommands":["work"],"message":"先去约会吧"}"""), context: JsonNode.Parse("""{"source":"test"}"""));

        Assert.True(createResult!["ok"]!.GetValue<bool>());
        var eventId = createResult["item"]!["id"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(eventId));

        var pollResult = await handler.PollDueTimelineEventsAsync("g1", nowEpochMs: 2000);
        Assert.True(pollResult!["ok"]!.GetValue<bool>());
        var items = pollResult["items"]!.AsArray();
        Assert.Single(items);
        Assert.Equal("active", items[0]!["status"]!.GetValue<string>());

        var ackResult = await handler.AckTimelineEventAsync("g1", eventId, "done");
        Assert.True(ackResult!["ok"]!.GetValue<bool>());

        var query = await handler.QueryTimelineEventsAsync("g1", status: "done");
        Assert.True(query!["ok"]!.GetValue<bool>());
        var doneItems = query["items"]!.AsArray();
        Assert.Single(doneItems);
        Assert.Equal(eventId, doneItems[0]!["id"]!.GetValue<string>());
        Assert.Equal("done", doneItems[0]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ack_DailyRecurrence_CreatesNextPendingEvent()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        _timeService.GetSnapshotAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 500,
            TimeStr = "08:20",
            Mode = TimeMode.Virtual,
            EpochMs = 2_000
        });

        var timelineService = new TimelineService(_connStr);
        var handler = new TimelineApiHandler(_stateRepo, timelineService, _timeService, _bridgeContextAccessor);

        var createResult = await handler.CreateTimelineEventAsync("g1", "class_reminder", dueAtEpochMs: 1000, title: "上课提醒", recurrenceRule: "daily");
        var eventId = createResult!["item"]!["id"]!.GetValue<string>();

        var pollResult = await handler.PollDueTimelineEventsAsync("g1", nowEpochMs: 2000);
        Assert.True(pollResult!["ok"]!.GetValue<bool>());
        Assert.Single(pollResult["items"]!.AsArray());

        var ackResult = await handler.AckTimelineEventAsync("g1", eventId, "done");
        Assert.True(ackResult!["ok"]!.GetValue<bool>());

        var pendingResult = await handler.QueryTimelineEventsAsync("g1", status: "pending");
        Assert.True(pendingResult!["ok"]!.GetValue<bool>());
        var pendingItems = pendingResult["items"]!.AsArray();
        Assert.Single(pendingItems);
        Assert.Equal("daily", pendingItems[0]!["recurrenceRule"]!.GetValue<string>());
        Assert.Equal(86_401_000, pendingItems[0]!["dueAtEpochMs"]!.GetValue<long>());
    }

    [Fact]
    public async Task Create_TooLargeActionPayload_ReturnsValidationError()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        _timeService.GetSnapshotAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 500,
            TimeStr = "08:20",
            Mode = TimeMode.Virtual,
            EpochMs = 2_000
        });

        var timelineService = new TimelineService(_connStr);
        var handler = new TimelineApiHandler(_stateRepo, timelineService, _timeService, _bridgeContextAccessor);

        var bigAction = new JsonObject { ["x"] = new string('a', TimelineLimits.MaxActionJsonBytes + 16) };
        var result = await handler.CreateTimelineEventAsync("g1", "date_invite", dueAtEpochMs: 1000, title: "测试", action: bigAction);
        

        Assert.False(result!["ok"]!.GetValue<bool>());
        Assert.Equal("action_payload_too_large", result["error"]!.GetValue<string>());

        var query = await handler.QueryTimelineEventsAsync("g1");
        Assert.True(query!["ok"]!.GetValue<bool>());
        Assert.Empty(query["items"]!.AsArray());
    }

    [Fact]
    public async Task Create_DisabledMode_ReturnsTimelineDisabled()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        _timeService.GetSnapshotAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 480,
            TimeStr = "08:00",
            Mode = TimeMode.Disabled,
            EpochMs = 0
        });

        var timelineService = new TimelineService(_connStr);
        var handler = new TimelineApiHandler(_stateRepo, timelineService, _timeService, _bridgeContextAccessor);

        var result = await handler.CreateTimelineEventAsync("g1", "date_invite", dueAtEpochMs: 1000, title: "约会");
        Assert.False(result!["ok"]!.GetValue<bool>());
        Assert.Equal("timeline_disabled", result["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task TimelineService_Create_RejectsOversizedContextPayload()
    {
        var timelineService = new TimelineService(_connStr);

        await Assert.ThrowsAsync<ArgumentException>(() => timelineService.CreateAsync("g1", new TimelineCreateRequest
        {
            EventType = "date_invite",
            Title = "title",
            DueAtEpochMs = 1000,
            ContextJson = new string('b', TimelineLimits.MaxContextJsonBytes + 8)
        }));
    }

    [Fact]
    public async Task Create_InvalidRecurrenceOrActionFormat_ReturnsValidationError()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 480,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        var timelineService = new TimelineService(_connStr);
        var handler = new TimelineApiHandler(_stateRepo, timelineService, _timeService, _bridgeContextAccessor);

        var invalidRecurrence = await handler.CreateTimelineEventAsync("g1", "class_reminder", dueAtEpochMs: 1000, title: "上课提醒", recurrenceRule: "every:0h");
        Assert.False(invalidRecurrence!["ok"]!.GetValue<bool>());
        Assert.Equal("recurrenceRule_invalid", invalidRecurrence["error"]!.GetValue<string>());
        var stringAction = JsonValue.Create("just-a-string");
        var invalidAction = await handler.CreateTimelineEventAsync("g1", "date_invite", dueAtEpochMs: 1000, title: "约会提醒", action: stringAction);
        Assert.False(invalidAction!["ok"]!.GetValue<bool>());
        Assert.Equal("action_payload_must_be_object", invalidAction["error"]!.GetValue<string>());
    }
}
