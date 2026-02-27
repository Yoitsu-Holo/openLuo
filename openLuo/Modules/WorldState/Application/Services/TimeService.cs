using openLuo.Modules.WorldState.Application.Services.TimeProviders;
using openLuo.Core;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.WorldState.Infrastructure.State;

namespace openLuo.Modules.WorldState.Application.Services;

public class TimeService : ITimeService
{
    private const string TimeNamespace = "system_time";
    private const string TimeOwnerId = "kernel";

    private readonly IGameStateRepository _stateRepo;
    private readonly IStateQueryService _stateQueryService;
    private readonly IStateRegistry _stateRegistry;
    private readonly IGameLogger _logger;
    private readonly IGameContextAccessor _gameContextAccessor;
    private readonly StateDefStore? _defStore;
    private readonly Dictionary<TimeMode, ITimeProvider> _providers;

    public TimeService(
        IGameStateRepository stateRepo,
        IStateQueryService stateQueryService,
        IStateRegistry stateRegistry,
        IGameLogger logger,
        IGameContextAccessor gameContextAccessor,
        StateDefStore? defStore = null,
        IEnumerable<ITimeProvider>? providers = null)
    {
        _stateRepo = stateRepo;
        _stateQueryService = stateQueryService;
        _stateRegistry = stateRegistry;
        _logger = logger;
        _gameContextAccessor = gameContextAccessor;
        _defStore = defStore;
        _providers = BuildProviderMap(providers);
        EnsureTimeStateDefsRegistered();
    }

    public async Task<TimeSnapshot?> GetSnapshotAsync(CancellationToken ct = default)
    {
        var gameId = _gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        var state = await _stateRepo.GetAsync(gameId, ct);
        if (state is null) return null;

        return await GetSnapshotAsync(state.Id, ct);
    }

    public async Task<TimeSnapshot?> GetSnapshotAsync(string gameId, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(gameId, ct);
        if (state is null) return null;

        var mode = await ReadModeAsync(state.Id);
        var timezone = await ReadTimezoneAsync(state.Id);
        return ResolveProvider(mode).GetSnapshot(state, timezone);
    }

    public async Task<TimeAdvanceResult> AdvanceAsync(int minutes, string source = "unknown", CancellationToken ct = default)
    {
        var gameId = _gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return new TimeAdvanceResult
            {
                Ok = false,
                RequestedMinutes = minutes,
                AppliedMinutes = 0,
                Reason = "no_active_game",
                Snapshot = new TimeSnapshot()
            };
        }

        var state = await _stateRepo.GetAsync(gameId, ct);
        if (state is null)
        {
            return new TimeAdvanceResult
            {
                Ok = false,
                RequestedMinutes = minutes,
                AppliedMinutes = 0,
                Reason = "no_active_game",
                Snapshot = new TimeSnapshot()
            };
        }

        return await AdvanceAsync(state.Id, minutes, source, ct);
    }

    public async Task<TimeAdvanceResult> AdvanceAsync(string gameId, int minutes, string source = "unknown", CancellationToken ct = default)
    {
        if (minutes <= 0)
        {
            var snapshot = await GetSnapshotAsync(gameId, ct) ?? new TimeSnapshot();
            return new TimeAdvanceResult
            {
                Ok = false,
                RequestedMinutes = minutes,
                AppliedMinutes = 0,
                Reason = "minutes_must_be_positive",
                Snapshot = snapshot
            };
        }

        var state = await _stateRepo.GetAsync(gameId, ct);
        if (state is null)
        {
            return new TimeAdvanceResult
            {
                Ok = false,
                RequestedMinutes = minutes,
                AppliedMinutes = 0,
                Reason = "no_active_game",
                Snapshot = new TimeSnapshot()
            };
        }

        var mode = await ReadModeAsync(state.Id);
        var timezone = await ReadTimezoneAsync(state.Id);
        var policy = await ReadRealtimeAdvancePolicyAsync(state.Id);
        var provider = ResolveProvider(mode);
        var result = provider.Advance(state, minutes, source, timezone, policy);

        if (mode == TimeMode.Virtual && result.Ok && result.AppliedMinutes > 0)
        {
            await _stateRepo.SaveAsync(state, ct);
            _logger?.Debug("time/advance", $"virtual +{result.AppliedMinutes}min source={source}",
                new
                {
                    mode = "virtual",
                    day = state.CurrentDay,
                    minute = state.CurrentMinute,
                    source,
                    requested = result.RequestedMinutes,
                    applied = result.AppliedMinutes
                });
            return result;
        }

        if (mode == TimeMode.Realtime)
        {
            await SyncRealtimeStateIfNeededAsync(state, result.Snapshot, ct);
            _logger?.Debug("time/advance", $"realtime advance no-op source={source}",
                new
                {
                    mode = "realtime",
                    source,
                    requested = result.RequestedMinutes,
                    applied = result.AppliedMinutes,
                    reason = result.Reason
                });
            return result;
        }

        _logger?.Debug("time/advance", $"disabled advance no-op source={source}",
            new
            {
                mode = "disabled",
                source,
                requested = result.RequestedMinutes,
                applied = result.AppliedMinutes,
                reason = result.Reason
            });
        return result;
    }

    public async Task<TimeSnapshot?> TickAsync(CancellationToken ct = default)
    {
        var gameId = _gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        var state = await _stateRepo.GetAsync(gameId, ct);
        if (state is null) return null;

        return await TickAsync(state.Id, ct);
    }

    public async Task<TimeSnapshot?> TickAsync(string gameId, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(gameId, ct);
        if (state is null) return null;

        var mode = await ReadModeAsync(state.Id);
        var timezone = await ReadTimezoneAsync(state.Id);
        var snapshot = ResolveProvider(mode).GetSnapshot(state, timezone);
        if (mode != TimeMode.Realtime)
            return snapshot;

        await SyncRealtimeStateIfNeededAsync(state, snapshot, ct);
        return snapshot;
    }

    private async Task<TimeMode> ReadModeAsync(string gameId)
    {
        EnsureTimeStateDefsRegistered();

        var stateValue = await _stateQueryService.GetAsync(
            gameId,
            TimeNamespace,
            StateOwnerKind.System,
            TimeOwnerId,
            "mode");

        var raw = stateValue.Value?.Trim().ToLowerInvariant();
        return raw switch
        {
            "realtime" => TimeMode.Realtime,
            "disabled" => TimeMode.Disabled,
            _ => TimeMode.Virtual
        };
    }

    private async Task<string> ReadTimezoneAsync(string gameId)
    {
        EnsureTimeStateDefsRegistered();

        var stateValue = await _stateQueryService.GetAsync(
            gameId,
            TimeNamespace,
            StateOwnerKind.System,
            TimeOwnerId,
            "timezone");

        var timezone = stateValue.Value?.Trim();
        return string.IsNullOrWhiteSpace(timezone) ? "local" : timezone;
    }

    private async Task<string> ReadRealtimeAdvancePolicyAsync(string gameId)
    {
        EnsureTimeStateDefsRegistered();

        var stateValue = await _stateQueryService.GetAsync(
            gameId,
            TimeNamespace,
            StateOwnerKind.System,
            TimeOwnerId,
            "realtime_advance_policy");

        var policy = stateValue.Value?.Trim();
        return string.IsNullOrWhiteSpace(policy) ? "no_op" : policy;
    }

    private void EnsureTimeStateDefsRegistered()
    {
        RegisterDefIfMissing(new StateDef
        {
            Namespace = TimeNamespace,
            Key = "mode",
            OwnerKind = StateOwnerKind.System,
            ValueType = StateValueType.Enum,
            DefaultValue = "virtual",
            EnumValues = ["virtual", "realtime", "disabled"],
            HiddenFromStatus = true,
            MutableByLlm = false,
            Derived = false,
            PluginId = "core_time_kernel",
            PromptContext = "时间模式：virtual/realtime/disabled。",
            MetadataJson = "{\"name\":\"时间模式\",\"category\":\"system\"}"
        });

        RegisterDefIfMissing(new StateDef
        {
            Namespace = TimeNamespace,
            Key = "timezone",
            OwnerKind = StateOwnerKind.System,
            ValueType = StateValueType.Text,
            DefaultValue = "local",
            HiddenFromStatus = true,
            MutableByLlm = false,
            Derived = false,
            PluginId = "core_time_kernel",
            PromptContext = "时区设置：local/utc 或系统时区 ID。",
            MetadataJson = "{\"name\":\"时区\",\"category\":\"system\"}"
        });

        RegisterDefIfMissing(new StateDef
        {
            Namespace = TimeNamespace,
            Key = "realtime_advance_policy",
            OwnerKind = StateOwnerKind.System,
            ValueType = StateValueType.Enum,
            DefaultValue = "no_op",
            EnumValues = ["no_op"],
            HiddenFromStatus = true,
            MutableByLlm = false,
            Derived = false,
            PluginId = "core_time_kernel",
            PromptContext = "现实时间模式下的 advance 策略。",
            MetadataJson = "{\"name\":\"现实时间推进策略\",\"category\":\"system\"}"
        });
    }

    private void RegisterDefIfMissing(StateDef def)
    {
        var existing = _stateRegistry.GetDef(def.Namespace, def.OwnerKind, def.Key);
        if (existing is not null) return;

        _stateRegistry.Register(def);
        try
        {
            _defStore?.Upsert(def);
        }
        catch (Exception ex)
        {
            _logger?.Warn("time/defs", $"defer persisting state def {def.Namespace}.{def.Key}: {ex.Message}");
        }
    }

    private static Dictionary<TimeMode, ITimeProvider> BuildProviderMap(IEnumerable<ITimeProvider>? providers)
    {
        var source = providers?.ToList() ?? [];
        if (source.Count == 0)
        {
            source =
            [
                new VirtualTimeProvider(),
                new RealtimeTimeProvider(),
                new DisabledTimeProvider()
            ];
        }

        var map = new Dictionary<TimeMode, ITimeProvider>();
        foreach (var provider in source)
            map[provider.Mode] = provider;
        return map;
    }

    private ITimeProvider ResolveProvider(TimeMode mode) =>
        _providers.TryGetValue(mode, out var provider)
            ? provider
            : _providers[TimeMode.Virtual];

    private async Task SyncRealtimeStateIfNeededAsync(GameState state, TimeSnapshot snapshot, CancellationToken ct)
    {
        if (state.CurrentDay == snapshot.Day && state.CurrentMinute == snapshot.Minute)
            return;

        state.CurrentDay = snapshot.Day;
        state.CurrentMinute = snapshot.Minute;
        await _stateRepo.SaveAsync(state, ct);
        _logger?.Debug("time/tick", "realtime snapshot synced to game_state",
            new { mode = "realtime", day = snapshot.Day, minute = snapshot.Minute });
    }
}
