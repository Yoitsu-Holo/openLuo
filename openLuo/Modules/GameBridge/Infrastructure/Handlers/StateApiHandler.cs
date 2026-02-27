using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using openLuo.Core.Interfaces;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.WorldState.Infrastructure.State;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class StateApiHandler(
    IGameStateRepository stateRepo,
    IStateRegistry stateRegistry,
    IStateMutationService mutationService,
    IStateQueryService queryService,
    IGameLogger logger,
    IGameBridgeContextAccessor bridgeContextAccessor,
    StateDefStore? defStore = null)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static bool TryParseOwnerKind(string? s, out StateOwnerKind kind) =>
        Enum.TryParse(s, true, out kind);

    private static bool TryParseValueType(string? s, out StateValueType valueType)
    {
        switch (s?.ToLowerInvariant())
        {
            case "number":
                valueType = StateValueType.Number;
                return true;
            case "enum":
                valueType = StateValueType.Enum;
                return true;
            case "text":
                valueType = StateValueType.Text;
                return true;
            case "json":
                valueType = StateValueType.Json;
                return true;
            case "asset_ref":
                valueType = StateValueType.AssetRef;
                return true;
            default:
                valueType = default;
                return false;
        }
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is not JsonValue value)
            return node?.ToJsonString();

        if (value.TryGetValue<string>(out var s)) return s;
        if (value.TryGetValue<int>(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<double>(out var d)) return d.ToString("G", CultureInfo.InvariantCulture);
        if (value.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        return node?.ToJsonString();
    }

    private static int ReadInt(JsonNode? node, int fallback = 0)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i)) return i;
            if (value.TryGetValue<long>(out var l)) return (int)l;
            if (value.TryGetValue<double>(out var d)) return (int)d;
        }

        var text = ReadString(node);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ReadBool(JsonNode? node, bool fallback = false)
    {
        if (node is JsonValue value && value.TryGetValue<bool>(out var b))
            return b;

        var text = ReadString(node);
        return bool.TryParse(text, out var parsed) ? parsed : fallback;
    }

    // ── game/state/register ────────────────────────────────────────────────────

    [GameApi("game/state/register")]
    public JsonNode? RegisterStateDef(string key, string @namespace, string valueType, string ownerKind = "game", string ownerId = "global", string? defaultValue = null, string? pluginId = null, bool mutableByLlm = true, bool derived = false, string? min = null, string? max = null, int statusOrder = 0, string? statusGroup = null, bool hiddenFromStatus = false, string? displayFormat = null, string? promptContext = null, JsonNode? metadata = null, JsonNode? enumValues = null)
    {
        if (string.IsNullOrWhiteSpace(key)
            || string.IsNullOrWhiteSpace(@namespace)
            || string.IsNullOrWhiteSpace(ownerKind)
            || string.IsNullOrWhiteSpace(valueType))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"invalid_state_def_required_fields\"}");

        if (!TryParseOwnerKind(ownerKind, out var parsedOwnerKind)
            || !TryParseValueType(valueType, out var parsedValueType))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"invalid_state_def_enum_values\"}");

        var metadataJson = metadata is not null
            ? JsonSerializer.Serialize(metadata, _serOpts)
            : null;

        var defObj = new StateDef
        {
            Namespace       = @namespace ?? string.Empty,
            Key             = key,
            OwnerKind       = parsedOwnerKind,
            ValueType       = parsedValueType,
            DefaultValue    = defaultValue,
            MinValue        = min,
            MaxValue        = max,
            MutableByLlm    = mutableByLlm,
            Derived         = derived,
            StatusGroup     = statusGroup,
            StatusOrder     = statusOrder,
            HiddenFromStatus = hiddenFromStatus,
            DisplayFormat   = displayFormat,
            PromptContext   = promptContext,
            PluginId        = pluginId,
            MetadataJson    = metadataJson,
            EnumValues      = (enumValues as JsonArray)
                              ?.Select(v => v?.GetValue<string>() ?? string.Empty)
                              .Where(v => !string.IsNullOrWhiteSpace(v))
                              .ToList() ?? []
        };

        stateRegistry.Register(defObj);
        defStore?.Upsert(defObj);
        logger?.Info("state/register", $"registered {defObj.DefinitionId}");

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok = true,
            definitionId = defObj.DefinitionId
        }, _serOpts));
    }

    // ── game/state/get ─────────────────────────────────────────────────────────

    [GameApi("game/state/get")]
    public async Task<JsonNode?> GetStateValueAsync(string gameId, string @namespace, string key, string ownerKind, string ownerId)
    {
        var gameState = await ResolveStateAsync(gameId);
        if (gameState is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        if (!TryParseOwnerKind(ownerKind, out var parsedOwnerKind))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"invalid_owner_kind\"}");

        var sv = await queryService.GetAsync(gameState.Id, @namespace, parsedOwnerKind, ownerId, key);

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok        = true,
            ns        = @namespace,
            key,
            ownerKind = parsedOwnerKind.ToString().ToLowerInvariant(),
            ownerId,
            value     = sv.Value,
            valueType = sv.ValueType,
            defaulted = sv.Defaulted,
            updatedAt = sv.UpdatedAt
        }, _serOpts));
    }

    // ── game/state/query ───────────────────────────────────────────────────────

    [GameApi("game/state/query")]
    public async Task<JsonNode?> QueryGameStatesAsync(string gameId, string? @namespace = null, string? ownerKind = null, string? ownerId = null, string[]? keys = null, bool includeDefaults = false)
    {
        var gameState = await ResolveStateAsync(gameId);
        if (gameState is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        StateOwnerKind? parsedOwnerKind = ownerKind is not null
            && TryParseOwnerKind(ownerKind, out var pk)
            ? pk
            : null;
        var keyList = keys?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        var items = await queryService.QueryAsync(
            gameState.Id, @namespace, parsedOwnerKind, ownerId, keyList, includeDefaults);

        var result = items.Select(sv => new
        {
            ns        = sv.Namespace,
            key       = sv.Key,
            ownerKind = sv.OwnerKind.ToString().ToLowerInvariant(),
            ownerId   = sv.OwnerId,
            value     = sv.Value,
            valueType = sv.ValueType
        }).ToList();

        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, items = result }, _serOpts));
    }

    // ── game/state/apply ───────────────────────────────────────────────────────

    [GameApi("game/state/apply")]
    public async Task<JsonNode?> ApplyStateMutationsAsync(string gameId, JsonNode? mutations)
    {
        var gameState = await ResolveStateAsync(gameId);
        if (gameState is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var mutationsNode = mutations as JsonArray;
        if (mutationsNode is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"missing mutations array\"}");

        var mutationList = mutationsNode.Select(m => new StateMutation
        {
            Namespace  = m?["namespace"]?.GetValue<string>() ?? string.Empty,
            Key        = m?["key"]?.GetValue<string>() ?? string.Empty,
            OwnerKind  = TryParseOwnerKind(m?["ownerKind"]?.GetValue<string>(), out var parsedMutationKind)
                ? parsedMutationKind
                : StateOwnerKind.Game,
            OwnerId    = m?["ownerId"]?.GetValue<string>() ?? string.Empty,
            Op         = m?["op"]?.GetValue<string>() ?? "set",
            Value      = m?["value"]?.GetValue<string>() ?? string.Empty,
            Reason     = m?["reason"]?.GetValue<string>(),
            SourceType = m?["sourceType"]?.GetValue<string>(),
            SourceId   = m?["sourceId"]?.GetValue<string>()
        }).ToList();

        var results = await mutationService.ApplyAsync(gameState.Id, mutationList);

        var resultDtos = results.Select(r => new
        {
            ns        = r.Namespace,
            key       = r.Key,
            ownerKind = r.OwnerKind.ToString().ToLowerInvariant(),
            ownerId   = r.OwnerId,
            oldValue  = r.OldValue,
            newValue  = r.NewValue,
            clamped   = r.Clamped,
            ok        = r.Ok,
            error     = r.Error
        }).ToList();

        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, results = resultDtos }, _serOpts));
    }

    private async Task<GameState?> ResolveStateAsync(string? gameId)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
            return await stateRepo.GetAsync(gameId);

        var contextGameId = bridgeContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(contextGameId))
            return await stateRepo.GetAsync(contextGameId);

        return null;
    }
}
