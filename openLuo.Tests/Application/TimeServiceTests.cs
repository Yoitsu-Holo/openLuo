using openLuo.Modules.WorldState.Application.Services;
using openLuo.Core.Interfaces;
using openLuo.Infrastructure.Runtime;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Application.Tests;

public class TimeServiceTests
{
    private readonly IGameStateRepository _stateRepo = Substitute.For<IGameStateRepository>();
    private readonly IStateQueryService _stateQuery = Substitute.For<IStateQueryService>();
    private readonly IStateRegistry _stateRegistry = Substitute.For<IStateRegistry>();
    private readonly IGameLogger _logger = Substitute.For<IGameLogger>();
    private readonly IGameContextAccessor _gameContextAccessor = Substitute.For<IGameContextAccessor>();

    private TimeService CreateService(string mode = "virtual", string timezone = "local", string policy = "no_op")
    {
        _stateQuery.GetAsync(
                Arg.Any<string>(),
                "system_time",
                StateOwnerKind.System,
                "kernel",
                Arg.Any<string>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(4);
                return new StateValue
                {
                    Namespace = "system_time",
                    Key = key,
                    OwnerKind = StateOwnerKind.System,
                    OwnerId = "kernel",
                    Value = key switch
                    {
                        "mode" => mode,
                        "timezone" => timezone,
                        "realtime_advance_policy" => policy,
                        _ => string.Empty
                    }
                };
            });

        _gameContextAccessor.Current = new GameRuntimeContext { GameId = "g1" };
        return new TimeService(_stateRepo, _stateQuery, _stateRegistry, _logger, _gameContextAccessor);
    }

    [Fact]
    public async Task AdvanceAsync_VirtualMode_AppliesMinutesAndPersists()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 1380, // 23:00
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.SaveAsync(Arg.Any<GameState>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = CreateService(mode: "virtual");
        var result = await service.AdvanceAsync(90, "test");

        Assert.True(result.Ok);
        Assert.Equal(90, result.RequestedMinutes);
        Assert.Equal(90, result.AppliedMinutes);
        Assert.Equal(2, result.Snapshot.Day);
        Assert.Equal(30, result.Snapshot.Minute);
        Assert.Equal("00:30", result.Snapshot.TimeStr);

        await _stateRepo.Received(1).SaveAsync(
            Arg.Is<GameState>(s => s.CurrentDay == 2 && s.CurrentMinute == 30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_RealtimeMode_IsNoOp()
    {
        var now = DateTime.UtcNow;
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.SaveAsync(Arg.Any<GameState>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = CreateService(mode: "realtime", timezone: "utc", policy: "no_op");
        var result = await service.AdvanceAsync(30, "test");

        Assert.True(result.Ok);
        Assert.Equal(0, result.AppliedMinutes);
        Assert.Equal("realtime_mode_no_op", result.Reason);
        Assert.Equal(TimeMode.Realtime, result.Snapshot.Mode);
    }

    [Fact]
    public async Task GetSnapshotAsync_DisabledMode_ReturnsDisabledSnapshot()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 3,
            CurrentMinute = 600,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);

        var service = CreateService(mode: "disabled");
        var snapshot = await service.GetSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(TimeMode.Disabled, snapshot!.Mode);
        Assert.Equal(3, snapshot.Day);
        Assert.Equal(600, snapshot.Minute);
        Assert.Equal("10:00", snapshot.TimeStr);
    }

    [Fact]
    public async Task AdvanceAsync_VirtualMode_24hSimulation_RemainsStable()
    {
        var state = new GameState
        {
            Id = "g1",
            PlayerName = "P",
            ArchetypeId = "bg1",
            CurrentDay = 1,
            CurrentMinute = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.GetAsync(Arg.Any<CancellationToken>()).Returns(state);
        _stateRepo.SaveAsync(Arg.Any<GameState>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var service = CreateService(mode: "virtual");

        for (var i = 0; i < 1_440; i++)
        {
            var result = await service.AdvanceAsync(1, "sim");
            Assert.True(result.Ok);
        }

        var snapshot = await service.GetSnapshotAsync();
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Day);
        Assert.Equal(0, snapshot.Minute);
    }
}
