using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Content.Application;
using openLuo.Core;
using openLuo.Core.Interfaces;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class PlayerApiHandler(
    IGameStateRepository stateRepo,
    ICharacterRepository characterRepo,
    IInventoryRepository inventoryRepository,
    IStateQueryService stateQueryService,
    IStateMutationService stateMutationService,
    ItemDefinitionCatalog itemCatalog,
    IGameContextAccessor gameContextAccessor,
    IGameBridgeContextAccessor bridgeContextAccessor)
{
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Dictionary<string, int> StageToAffection = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Stranger"] = 0,
        ["Acquaintance"] = 200,
        ["Friend"] = 400,
        ["CloseFriend"] = 600,
        ["Lover"] = 800,
        ["陌生人"] = 0,
        ["熟人"] = 200,
        ["朋友"] = 400,
        ["好友"] = 600,
        ["恋人"] = 800
    };

    private static string RelationshipStageLabel(RelationshipStage stage) => stage switch
    {
        RelationshipStage.Stranger => "陌生人",
        RelationshipStage.Acquaintance => "熟人",
        RelationshipStage.Friend => "朋友",
        RelationshipStage.CloseFriend => "好友",
        RelationshipStage.Lover => "恋人",
        _ => "陌生人"
    };

    private static int ParseAffection(string? rawValue)
    {
        if (int.TryParse(rawValue, out var affection))
            return affection;

        if (double.TryParse(rawValue, out var affectionDouble))
            return (int)affectionDouble;

        return 0;
    }

    private async Task<string> ResolveRelationshipStageAsync(string gameId, string characterId)
    {
        var stageState = await stateQueryService.GetAsync(gameId, "char_status", StateOwnerKind.Character, characterId, "relationship_stage");
        if (!string.IsNullOrWhiteSpace(stageState.Value))
            return stageState.Value;

        var affectionState = await stateQueryService.GetAsync(gameId, "char_status", StateOwnerKind.Character, characterId, "affection");
        var affection = ParseAffection(affectionState.Value);
        return RelationshipStageLabel(GameConstants.GetRelationshipStageForAffection(affection));
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

        var contextGameId = bridgeContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(contextGameId))
            return await stateRepo.GetAsync(contextGameId);

        var gameContextGameId = gameContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(gameContextGameId))
            return await stateRepo.GetAsync(gameContextGameId);

        return null;
    }

    [GameApi("game/character/get")]
    public async Task<JsonNode?> GetPlayerCharacterAsync(string gameId)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null) return null;
        var character = await ResolveActiveCharacterAsync(state);
        if (character is null) return null;

        var relationshipStage = await ResolveRelationshipStageAsync(state.Id, character.Id);
        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            character.Id, character.Name,
            RelationshipStage = relationshipStage
        }, _serOpts));
    }

    [GameApi("game/character/update")]
    public async Task<JsonNode?> UpdatePlayerCharacterAsync(string gameId, string relationshipStage)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null) return JsonNode.Parse("{\"ok\":false,\"error\":\"game_not_initialized\"}");
        var character = await ResolveActiveCharacterAsync(state);
        if (character is null) return JsonNode.Parse("{\"ok\":false,\"error\":\"character_not_found\"}");

        if (string.IsNullOrWhiteSpace(relationshipStage))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"relationship_stage_required\"}");

        if (!StageToAffection.TryGetValue(relationshipStage, out var targetAffection))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"invalid_relationship_stage\"}");

        var results = await stateMutationService.ApplyAsync(state.Id, [
            new StateMutation
            {
                Namespace = "char_status",
                Key = "affection",
                OwnerKind = StateOwnerKind.Character,
                OwnerId = character.Id,
                Op = "set",
                Value = targetAffection.ToString(),
                Reason = "manual_character_update",
                SourceType = "api",
                SourceId = "game/character/update"
            }
        ]);

        var applyResult = results.FirstOrDefault();
        if (applyResult is null || !applyResult.Ok)
            return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, error = applyResult?.Error ?? "state_apply_failed" }, _serOpts));

        var resolvedRelationshipStage = await ResolveRelationshipStageAsync(state.Id, character.Id);
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, relationshipStage = resolvedRelationshipStage }, _serOpts));
    }

    [GameApi("game/inventory/get")]
    public async Task<JsonNode?> GetPlayerInventoryAsync(string gameId)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null) return JsonNode.Parse("[]");

        var inv = await inventoryRepository.GetAllAsync(state.Id);
        return JsonNode.Parse(JsonSerializer.Serialize(
            inv.Select(kv => new { itemId = kv.Key, quantity = kv.Value }), _serOpts));
    }

    [GameApi("game/inventory/add")]
    public async Task<JsonNode?> AddPlayerInventoryAsync(string gameId, string itemId, int quantity = 1)
    {
        if (string.IsNullOrEmpty(itemId)) return JsonNode.Parse("{\"ok\":false}");

        var state = await ResolveStateAsync(gameId);
        if (state is null) return JsonNode.Parse("{\"ok\":false}");

        await inventoryRepository.AddItemAsync(state.Id, itemId, quantity);
        return JsonNode.Parse("{\"ok\":true}");
    }

    [GameApi("game/inventory/remove")]
    public async Task<JsonNode?> RemovePlayerInventoryAsync(string gameId, string itemId, int quantity = 1)
    {
        if (string.IsNullOrEmpty(itemId)) return JsonNode.Parse("{\"ok\":false}");

        var state = await ResolveStateAsync(gameId);
        if (state is null) return JsonNode.Parse("{\"ok\":false}");

        var ok = await inventoryRepository.RemoveItemAsync(state.Id, itemId, quantity);
        return ok ? JsonNode.Parse("{\"ok\":true}") : JsonNode.Parse("{\"ok\":false,\"error\":\"not_found\"}");
    }

    [GameApi("game/items/list")]
    public JsonNode? ListAllItems()
    {
        var items = itemCatalog.AllItems.Select(i => new
        {
            i.Id,
            Name = i.DisplayName,
            i.Description,
            i.Price,
            i.Tags,
            i.AffectionDelta, i.MoodEffect,
            i.Rarity
        });
        return JsonNode.Parse(JsonSerializer.Serialize(items, _serOpts));
    }

    [GameApi("game/affection/record")]
    public async Task<JsonNode?> RecordPlayerAffectionAsync(string characterId, string reason, int delta = 0)
    {
        await characterRepo.RecordAffectionEventAsync(new AffectionEvent
        {
            Id = Guid.NewGuid().ToString(),
            CharacterId = characterId,
            Reason = reason,
            Delta = delta,
            OccurredAt = DateTime.UtcNow
        });
        return JsonNode.Parse("{\"ok\":true}");
    }
}
