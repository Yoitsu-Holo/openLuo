using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public sealed class ResourceApiHandler(
    IGameStateRepository stateRepo,
    ICharacterRepository characterRepo,
    IResourceCatalogService catalogService,
    IResourceValueService valueService,
    IResourceStatusProjectionService statusProjectionService,
    IResourceLifecycleService lifecycleService,
    IGameContextAccessor gameContextAccessor,
    IGameBridgeContextAccessor bridgeContextAccessor)
{
    private static readonly JsonSerializerOptions SerOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [GameApi("game/resource/definitions")]
    public async Task<JsonNode?> ListResourceDefinitionsAsync(string? @namespace = null, string? ownerKind = null, string? pluginId = null, bool visibleInStatusOnly = false, bool mutableByLlmOnly = false)
    {
        var query = new ResourceDefinitionQuery
        {
            Namespace = @namespace,
            OwnerKind = TryParseOwnerKind(ownerKind, out var parsedOwnerKind) ? parsedOwnerKind : null,
            PluginId = pluginId,
            VisibleInStatusOnly = visibleInStatusOnly,
            MutableByLlmOnly = mutableByLlmOnly
        };

        var defs = await catalogService.ListDefinitionsAsync(query);
        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok = true,
            definitions = defs
        }, SerOpts));
    }

    [GameApi("game/resource/status")]
    public async Task<JsonNode?> GetResourceStatusAsync(string gameId, string? characterId = null, bool includeHidden = false, bool includePluginItems = true)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        Character? character = null;
        if (!string.IsNullOrWhiteSpace(characterId))
            character = await characterRepo.GetByIdAsync(state.Id, characterId);

        character ??= await ResolveActiveCharacterAsync(state);
        if (character is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"character_not_found\"}");

        var snapshot = await statusProjectionService.BuildStatusSnapshotAsync(new ResourceStatusQuery
        {
            GameId = state.Id,
            CharacterId = character.Id,
            ArchetypeId = character.ArchetypeId,
            IncludeHidden = includeHidden,
            IncludePluginItems = includePluginItems
        });

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok = true,
            gameId = state.Id,
            character = new
            {
                character.Id,
                character.Name,
                character.ArchetypeId
            },
            items = snapshot.Items,
            additionalText = snapshot.AdditionalText
        }, SerOpts));
    }

    [GameApi("game/resource/values")]
    public async Task<JsonNode?> QueryResourceValuesAsync(string gameId, string? @namespace = null, string? ownerKind = null, string? ownerId = null, string[]? keys = null, bool includeDefaults = false)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        var values = await valueService.QueryValuesAsync(new ResourceValueQuery
        {
            GameId = state.Id,
            Namespace = @namespace,
            OwnerKind = TryParseOwnerKind(ownerKind, out var parsedKind) ? parsedKind : null,
            OwnerId = ownerId,
            Keys = keys,
            IncludeDefaults = includeDefaults
        });

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok = true,
            gameId = state.Id,
            values
        }, SerOpts));
    }

    [GameApi("game/resource/lifecycle")]
    public async Task<JsonNode?> UpdateResourceLifecycleAsync(string definitionId, string lifecycleState, string? retirementPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"missing_definition_id\"}");

        if (!TryParseLifecycle(lifecycleState, out var parsedLifecycleState))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"invalid_lifecycle_state\"}");

        var parsedRetirementPolicy = TryParseRetirement(retirementPolicy, out var prp)
            ? prp
            : ResourceRetirementPolicy.KeepValue;

        var updated = await lifecycleService.UpdateLifecycleAsync(new ResourceLifecycleUpdate
        {
            DefinitionId = definitionId,
            LifecycleState = parsedLifecycleState,
            RetirementPolicy = parsedRetirementPolicy
        });

        if (updated is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"definition_not_found\"}");

        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            ok = true,
            definition = updated
        }, SerOpts));
    }

    private async Task<Character?> ResolveActiveCharacterAsync(GameState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ActiveCharacterId))
        {
            var byId = await characterRepo.GetByIdAsync(state.Id, state.ActiveCharacterId);
            if (byId is not null)
                return byId;
        }

        return await characterRepo.GetByArchetypeIdAsync(state.ArchetypeId);
    }

    private async Task<GameState?> ResolveStateAsync(string? gameId = null)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
            return await stateRepo.GetAsync(gameId);

        var bridgeGameId = bridgeContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(bridgeGameId))
            return await stateRepo.GetAsync(bridgeGameId);

        var contextGameId = gameContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(contextGameId))
            return await stateRepo.GetAsync(contextGameId);

        return null;
    }

    private static bool TryParseOwnerKind(string? value, out StateOwnerKind ownerKind) =>
        Enum.TryParse(value, ignoreCase: true, out ownerKind);

    private static bool TryParseLifecycle(string? value, out ResourceLifecycleState state)
    {
        state = value?.Trim().ToLowerInvariant() switch
        {
            "active" => ResourceLifecycleState.Active,
            "hidden" => ResourceLifecycleState.Hidden,
            "frozen" => ResourceLifecycleState.Frozen,
            "retired" => ResourceLifecycleState.Retired,
            _ => default
        };
        return value?.Trim().ToLowerInvariant() is "active" or "hidden" or "frozen" or "retired";
    }

    private static bool TryParseRetirement(string? value, out ResourceRetirementPolicy policy)
    {
        policy = value?.Trim().ToLowerInvariant() switch
        {
            "keep_value" => ResourceRetirementPolicy.KeepValue,
            "hide_value" => ResourceRetirementPolicy.HideValue,
            "purge_value" => ResourceRetirementPolicy.PurgeValue,
            _ => default
        };
        return value?.Trim().ToLowerInvariant() is "keep_value" or "hide_value" or "purge_value";
    }
}
