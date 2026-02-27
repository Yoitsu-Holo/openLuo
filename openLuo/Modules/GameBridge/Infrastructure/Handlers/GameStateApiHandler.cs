using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class GameStateApiHandler(
    IGameStateRepository stateRepo,
    ITimeService timeService,
    IGameContextAccessor gameContextAccessor,
    IGameBridgeContextAccessor bridgeContextAccessor)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GameApi("game/session/get")]
    public async Task<JsonNode?> GetGameStateAsync(string gameId)
    {
        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId
            : bridgeContextAccessor.Current?.GameId
            ?? gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(resolvedGameId)) return null;
        var state = await stateRepo.GetAsync(resolvedGameId);
        if (state is null) return null;

        var snapshot = await timeService.GetSnapshotAsync(state.Id);
        var day = snapshot?.Day ?? state.CurrentDay;
        var minute = snapshot?.Minute ?? state.CurrentMinute;
        var timeStr = snapshot?.TimeStr ?? $"{state.CurrentMinute / 60:D2}:{state.CurrentMinute % 60:D2}";
        var mode = (snapshot?.Mode ?? TimeMode.Virtual).ToString().ToLowerInvariant();

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            state.Id, state.PlayerName, state.ArchetypeId, state.ActiveCharacterId,
            CurrentDay = day,
            CurrentMinute = minute,
            TimeStr = timeStr,
            Mode = mode,
            state.LastInteractionDay, state.CreatedAt, state.UpdatedAt
        }, _serOpts));
    }

    [GameApi("game/session/update")]
    public async Task<JsonNode?> UpdateGameStateAsync(string gameId, string? playerName = null, string? archetypeId = null, string? activeCharacterId = null)
    {
        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId
            : bridgeContextAccessor.Current?.GameId
            ?? gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(resolvedGameId)) return JsonNode.Parse("{\"ok\":false}");
        var state = await stateRepo.GetAsync(resolvedGameId);
        if (state is null) return JsonNode.Parse("{\"ok\":false}");
        if (playerName is not null) state.PlayerName = playerName;
        if (archetypeId is not null) state.ArchetypeId = archetypeId;
        if (activeCharacterId is not null) state.ActiveCharacterId = activeCharacterId;
        await stateRepo.SaveAsync(state);
        return JsonNode.Parse("{\"ok\":true}");
    }

    [GameApi("game/time/get")]
    public async Task<JsonNode?> GetGameTimeAsync(string gameId)
    {
        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId
            : bridgeContextAccessor.Current?.GameId
            ?? gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(resolvedGameId)) return null;
        var state = await stateRepo.GetAsync(resolvedGameId);
        if (state is null) return null;

        var snapshot = await timeService.GetSnapshotAsync(state.Id);
        var day = snapshot?.Day ?? state.CurrentDay;
        var minute = snapshot?.Minute ?? state.CurrentMinute;
        var timeStr = snapshot?.TimeStr ?? $"{state.CurrentMinute / 60:D2}:{state.CurrentMinute % 60:D2}";
        var isLate = snapshot?.IsLate ?? state.CurrentMinute >= 1320;
        var mode = (snapshot?.Mode ?? TimeMode.Virtual).ToString().ToLowerInvariant();
        var epochMs = snapshot?.EpochMs ?? 0;

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            Day = day,
            Minute = minute,
            TimeStr = timeStr,
            IsLate = isLate,
            Mode = mode,
            EpochMs = epochMs
        }, _serOpts));
    }

    [GameApi("game/time/advance")]
    public async Task<JsonNode?> AdvanceGameTimeAsync(string gameId, int minutes)
    {
        if (minutes <= 0) return JsonNode.Parse("{\"ok\":false,\"error\":\"minutes must be positive\"}");

        var resolvedGameId = !string.IsNullOrWhiteSpace(gameId) ? gameId
            : bridgeContextAccessor.Current?.GameId
            ?? gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(resolvedGameId)) return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");
        var state = await stateRepo.GetAsync(resolvedGameId);
        if (state is null) return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var advance = await timeService.AdvanceAsync(state.Id, minutes, source: "game/time/advance");
        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            Ok = advance.Ok,
            Day = advance.Snapshot.Day,
            Minute = advance.Snapshot.Minute,
            TimeStr = advance.Snapshot.TimeStr,
            Mode = advance.Snapshot.Mode.ToString().ToLowerInvariant(),
            AppliedMinutes = advance.AppliedMinutes,
            Reason = advance.Reason
        }, _serOpts));
    }
}
