using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class TimelineApiHandler(
    IGameStateRepository stateRepo,
    ITimelineService timelineService,
    ITimeService timeService,
    IGameBridgeContextAccessor bridgeContextAccessor)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async Task<GameState?> ResolveStateAsync(string? gameId = null)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
            return await stateRepo.GetAsync(gameId);

        var contextGameId = bridgeContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(contextGameId))
            return await stateRepo.GetAsync(contextGameId);

        return null;
    }

    [GameApi("game/timeline/create")]
    public async Task<JsonNode?> CreateTimelineEventAsync(string gameId, string eventType, long dueAtEpochMs, string? title = null, long? endAtEpochMs = null, string? recurrenceRule = null, JsonNode? action = null, JsonNode? context = null)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var snapshot = await timeService.GetSnapshotAsync(state.Id);
        if (snapshot?.Mode == TimeMode.Disabled)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"timeline_disabled\"}");

        if (string.IsNullOrWhiteSpace(eventType))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"eventType_required\"}");

        if (dueAtEpochMs <= 0)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"dueAtEpochMs_must_be_positive\"}");

        var req = new TimelineCreateRequest
        {
            EventType = eventType,
            Title = title ?? eventType,
            DueAtEpochMs = dueAtEpochMs,
            EndAtEpochMs = endAtEpochMs,
            RecurrenceRule = recurrenceRule,
            ActionJson = NodeToJsonText(action),
            ContextJson = NodeToJsonText(context)
        };

        if (!TimelineLimits.TryValidateCreateRequest(req, out var validationError))
            return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, error = validationError }, _serOpts));

        TimelineEvent created;
        try
        {
            created = await timelineService.CreateAsync(state.Id, req);
        }
        catch (ArgumentException ex) when (!string.IsNullOrWhiteSpace(ex.Message))
        {
            return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, error = ex.Message }, _serOpts));
        }
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, item = created }, _serOpts));
    }

    [GameApi("game/timeline/query")]
    public async Task<JsonNode?> QueryTimelineEventsAsync(string gameId, string? eventType = null, string? status = null, int limit = 50, int offset = 0)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var options = new TimelineQueryOptions
        {
            EventType = eventType,
            Status = status,
            Limit = limit,
            Offset = offset
        };

        var items = await timelineService.QueryAsync(state.Id, options);
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, items }, _serOpts));
    }

    [GameApi("game/timeline/poll_due")]
    public async Task<JsonNode?> PollDueTimelineEventsAsync(string gameId, long? nowEpochMs = null, int limit = 32)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var snapshot = await timeService.GetSnapshotAsync(state.Id);
        if (snapshot?.Mode == TimeMode.Disabled)
            return JsonNode.Parse("{\"ok\":true,\"nowEpochMs\":0,\"items\":[]}");

        var resolvedNowEpochMs = nowEpochMs
            ?? snapshot?.EpochMs
            ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var items = await timelineService.PollDueAsync(state.Id, resolvedNowEpochMs, limit);
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, nowEpochMs = resolvedNowEpochMs, items }, _serOpts));
    }

    [GameApi("game/timeline/ack")]
    public async Task<JsonNode?> AckTimelineEventAsync(string gameId, string eventId, string? status = null)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        if (string.IsNullOrWhiteSpace(eventId))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"eventId_required\"}");

        var finalStatus = status ?? TimelineEventStatus.Done;
        var ok = await timelineService.AckAsync(state.Id, eventId, finalStatus);
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok }, _serOpts));
    }

    [GameApi("game/timeline/cancel")]
    public async Task<JsonNode?> CancelTimelineEventAsync(string gameId, string eventId)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        if (string.IsNullOrWhiteSpace(eventId))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"eventId_required\"}");

        var ok = await timelineService.CancelAsync(state.Id, eventId);
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok }, _serOpts));
    }

    private static string? NodeToJsonText(JsonNode? node)
    {
        if (node is null) return null;
        return node is JsonValue value && value.TryGetValue<string>(out var s)
            ? s
            : node.ToJsonString();
    }

    private static long? ReadLong(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<double>(out var d)) return (long)d;
        if (value.TryGetValue<string>(out var s) && long.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }
}
