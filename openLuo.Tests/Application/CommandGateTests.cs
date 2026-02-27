using openLuo.Modules.Gameplay.Application.Services;
using openLuo.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Application.Tests;

public class CommandGateTests
{
    private readonly IGameStateRepository _stateRepo = Substitute.For<IGameStateRepository>();
    private readonly ITimeService _timeService = Substitute.For<ITimeService>();
    private readonly ITimelineService _timeline = Substitute.For<ITimelineService>();
    private readonly IPluginHost _plugins = Substitute.For<IPluginHost>();
    private readonly IGameLogger _logger = Substitute.For<IGameLogger>();

    private CommandGate CreateGate() =>
        new(_stateRepo, _timeService, _timeline, _plugins, _logger);

    private static GameState MakeState() => new()
    {
        Id = "g1",
        PlayerName = "P",
        ArchetypeId = "bg1",
        CurrentDay = 1,
        CurrentMinute = 480,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task BeforeExecute_DisabledMode_SkipsTimelinePoll()
    {
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeState());
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(MakeState());
        _timeService.TickAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 600,
            TimeStr = "10:00",
            Mode = TimeMode.Disabled,
            EpochMs = 1_000
        });

        var result = await CreateGate().BeforeExecuteAsync(new CommandGateContext
        {
            GameId = "g1",
            CommandName = "chat"
        });

        Assert.True(result.Allow);
        await _timeline.DidNotReceive().PollDueAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeforeExecute_ClassWindow_BlocksNonWhitelistedCommand()
    {
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeState());
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(MakeState());
        _timeService.TickAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 600, // 10:00
            TimeStr = "10:00",
            Mode = TimeMode.Virtual,
            EpochMs = 1_000
        });
        _timeline.PollDueAsync("g1", 1_000, 32, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateGate().BeforeExecuteAsync(new CommandGateContext
        {
            GameId = "g1",
            CommandName = "work"
        });

        Assert.False(result.Allow);
        Assert.Contains("上课时段", result.Message);
        await _timeline.Received(1).PollDueAsync("g1", 1_000, 32, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeforeExecute_LunchWindow_BlocksWorkAndAddsNotice()
    {
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeState());
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(MakeState());
        _timeService.TickAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 3,
            Minute = 730, // 12:10
            TimeStr = "12:10",
            Mode = TimeMode.Virtual,
            EpochMs = 1_000
        });
        _timeline.PollDueAsync("g1", 1_000, 32, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateGate().BeforeExecuteAsync(new CommandGateContext
        {
            GameId = "g1",
            CommandName = "work"
        });

        Assert.False(result.Allow);
        Assert.Contains("午餐时段", result.Message);
        Assert.Contains(result.Notices, x => x.Contains("午餐时间"));
        await _timeline.Received(1).PollDueAsync("g1", 1_000, 32, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeforeExecute_DueEvent_MergesActionAndHookNotice()
    {
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeState());
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(MakeState());
        _timeService.TickAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 500,
            TimeStr = "08:20",
            Mode = TimeMode.Virtual,
            EpochMs = 2_000
        });
        _timeline.PollDueAsync("g1", 2_000, 32, Arg.Any<CancellationToken>())
            .Returns([
                new TimelineEvent
                {
                    Id = "e1",
                    GameId = "g1",
                    EventType = "date_invite",
                    Title = "约会",
                    DueAtEpochMs = 1_000,
                    Status = "active",
                    ActionJson = "{\"message\":\"计划已到期\"}"
                }
            ]);
        _plugins.CallHookAsync("onScheduleDue", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<GameBridgeRequestContext?>())
            .Returns(new PluginHookResult { AdditionalText = "Hook提醒" });

        var result = await CreateGate().BeforeExecuteAsync(new CommandGateContext
        {
            GameId = "g1",
            CommandName = "chat"
        });

        Assert.True(result.Allow);
        Assert.Contains(result.Notices, x => x.Contains("计划已到期"));
        Assert.Contains(result.Notices, x => x.Contains("Hook提醒"));
    }

    [Fact]
    public async Task BeforeExecute_ClassWindow_StillProcessesDueEventsAndAutoAcks()
    {
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeState());
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(MakeState());
        _timeService.TickAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 2,
            Minute = 600, // class window
            TimeStr = "10:00",
            Mode = TimeMode.Virtual,
            EpochMs = 3_000
        });
        _timeline.PollDueAsync("g1", 3_000, 32, Arg.Any<CancellationToken>())
            .Returns([
                new TimelineEvent
                {
                    Id = "due-1",
                    GameId = "g1",
                    EventType = "date_invite",
                    Title = "约会提醒",
                    DueAtEpochMs = 2_000,
                    Status = TimelineEventStatus.Active,
                    ActionJson = "{\"message\":\"\\u001b[31m到点了\\u001b[0m\"}"
                }
            ]);
        _plugins.CallHookAsync("onScheduleDue", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<GameBridgeRequestContext?>())
            .Returns(new PluginHookResult { AdditionalText = "\u001b[33mHook提醒\u001b[0m" });

        var result = await CreateGate().BeforeExecuteAsync(new CommandGateContext
        {
            GameId = "g1",
            CommandName = "work"
        });

        Assert.False(result.Allow);
        Assert.Contains("上课时段", result.Message);
        Assert.Contains(result.Notices, x => x.Contains("到点了"));
        Assert.DoesNotContain(result.Notices, x => x.Contains("\u001b["));
        await _timeline.Received(1).AckAsync("g1", "due-1", TimelineEventStatus.Done, Arg.Any<CancellationToken>());
        await _plugins.Received(1).CallHookAsync("onScheduleDue", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<GameBridgeRequestContext?>());
    }

    [Fact]
    public async Task BeforeExecute_WhenAutoAckReturnsFalse_FallbackCancelsEvent()
    {
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeState());
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(MakeState());
        _timeService.TickAsync("g1", Arg.Any<CancellationToken>()).Returns(new TimeSnapshot
        {
            Day = 1,
            Minute = 520,
            TimeStr = "08:40",
            Mode = TimeMode.Virtual,
            EpochMs = 2_200
        });
        _timeline.PollDueAsync("g1", 2_200, 32, Arg.Any<CancellationToken>())
            .Returns([
                new TimelineEvent
                {
                    Id = "due-2",
                    GameId = "g1",
                    EventType = "date_invite",
                    Title = "约会提醒",
                    DueAtEpochMs = 2_000,
                    Status = TimelineEventStatus.Active
                }
            ]);
        _timeline.AckAsync("g1", "due-2", TimelineEventStatus.Done, Arg.Any<CancellationToken>())
            .Returns(false);
        _timeline.CancelAsync("g1", "due-2", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateGate().BeforeExecuteAsync(new CommandGateContext
        {
            GameId = "g1",
            CommandName = "chat"
        });

        Assert.True(result.Allow);
        await _timeline.Received(1).AckAsync("g1", "due-2", TimelineEventStatus.Done, Arg.Any<CancellationToken>());
        await _timeline.Received(1).CancelAsync("g1", "due-2", Arg.Any<CancellationToken>());
    }
}
