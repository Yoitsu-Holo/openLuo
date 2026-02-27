using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Content.Application;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Core;
using openLuo.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Infrastructure.Chat;
using openLuo.Modules.Memory.Core.Interfaces;

namespace openLuo.Modules.Gameplay.Application.Services;

public sealed class GiftExecutionView
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string CharacterName { get; init; } = string.Empty;
    public string Reply { get; init; } = string.Empty;
    public int AffectionDelta { get; init; }
    public int CurrentAffection { get; init; }
    public string RelationshipStage { get; init; } = string.Empty;

    public static GiftExecutionView Fail(string error) => new() { Success = false, Error = error };

    public static GiftExecutionView Ok(
        string itemId,
        string itemName,
        string characterName,
        string reply,
        int affectionDelta,
        int currentAffection,
        string relationshipStage) => new()
        {
            Success = true,
            ItemId = itemId,
            ItemName = itemName,
            CharacterName = characterName,
            Reply = reply,
            AffectionDelta = affectionDelta,
            CurrentAffection = currentAffection,
            RelationshipStage = relationshipStage
        };
}

public sealed class GiftService(
    IGameStateRepository stateRepo,
    ICharacterRepository characterRepo,
    IInventoryRepository inventoryRepository,
    IStateMutationService stateMutationService,
    IStateQueryService stateQueryService,
    ItemDefinitionCatalog itemCatalog,
    CharacterArchetypeCatalog archetypeCatalog,
    ILlmClient llmClient,
    IGameLogger logger,
    IMemoryWriteService memoryWriteService,
    IGameContextAccessor gameContextAccessor)
{
    private async Task<Character?> ResolveActiveCharacterAsync(GameState state, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(state.ActiveCharacterId))
        {
            var byId = await characterRepo.GetByIdAsync(state.Id, state.ActiveCharacterId, ct);
            if (byId is not null)
                return byId;
        }
        return await characterRepo.GetByArchetypeIdAsync(state.ArchetypeId, ct);
    }

    private static string RelationshipStageLabel(int affection)
    {
        var stage = GameConstants.GetRelationshipStageForAffection(affection);
        return stage switch
        {
            RelationshipStage.Stranger => "陌生人",
            RelationshipStage.Acquaintance => "熟人",
            RelationshipStage.Friend => "朋友",
            RelationshipStage.CloseFriend => "好友",
            RelationshipStage.Lover => "恋人",
            _ => "陌生人"
        };
    }

    public async Task<GiftExecutionView> ExecuteAsync(string itemRef, CancellationToken ct = default)
    {
        var state = await ResolveCurrentStateAsync(ct);
        if (state is null)
            return GiftExecutionView.Fail("游戏未初始化。");

        return await ExecuteAsync(state.Id, itemRef, ct);
    }

    public async Task<GiftExecutionView> ExecuteAsync(string gameId, string itemRef, CancellationToken ct = default)
    {
        var state = await stateRepo.GetAsync(gameId, ct);
        if (state is null)
            return GiftExecutionView.Fail("游戏未初始化。");

        var character = await ResolveActiveCharacterAsync(state, ct);
        if (character is null)
            return GiftExecutionView.Fail("角色数据不存在。");

        var item = itemCatalog.FindByReference(itemRef);
        if (item is null)
            return GiftExecutionView.Fail($"未找到物品「{itemRef}」，输入 /inventory 查看背包");

        var removed = await inventoryRepository.RemoveItemAsync(state.Id, item.Id, 1, ct);
        if (!removed)
            return GiftExecutionView.Fail($"背包中没有「{item.DisplayName}」，先去 /shop 购买吧");

        var moodState = await stateQueryService.GetAsync(state.Id, "char_status", StateOwnerKind.Character, character.Id, "mood");
        var currentMood = moodState.Value ?? "Neutral";
        var delta = item.AffectionDelta;
        if (currentMood.Equals("Angry", StringComparison.OrdinalIgnoreCase)
            && !item.Tags.Contains("special", StringComparer.OrdinalIgnoreCase))
        {
            delta /= 2;
        }

        var result = await stateMutationService.ApplyAsync(state.Id,
        [
            new StateMutation
            {
                Namespace = "char_status",
                Key = "affection",
                OwnerKind = StateOwnerKind.Character,
                OwnerId = character.Id,
                Op = "delta",
                Value = delta.ToString(),
                SourceType = "gift",
                SourceId = item.Id
            }
        ]);
        var newAffectionText = result.FirstOrDefault()?.NewValue ?? "0";

        if (!string.IsNullOrWhiteSpace(item.MoodEffect))
        {
            await stateMutationService.ApplyAsync(state.Id,
            [
                new StateMutation
                {
                    Namespace = "char_status",
                    Key = "mood",
                    OwnerKind = StateOwnerKind.Character,
                    OwnerId = character.Id,
                    Op = "set",
                    Value = item.MoodEffect!,
                    SourceType = "gift",
                    SourceId = item.Id
                }
            ]);
        }

        await characterRepo.RecordAffectionEventAsync(new AffectionEvent
        {
            Id = Guid.NewGuid().ToString(),
            CharacterId = character.Id,
            Reason = $"收到礼物：{item.DisplayName}",
            Delta = delta,
            OccurredAt = DateTime.UtcNow
        }, ct);

        await memoryWriteService.WriteAsync(new openLuo.Modules.Memory.Core.Models.MemoryWriteInput
        {
            GameId = state.Id,
            CharacterId = character.Id,
            Scope = openLuo.Modules.Memory.Core.Models.MemoryScope.CharacterPrivate,
            RawContent = $"{character.Name} 收到了玩家赠送的礼物：{item.DisplayName}。好感度变化 {delta:+#;-#;0}，当前好感度 {newAffectionText}。",
            Source = "gameplay/gift",
            Emotion = delta > 0
                ? openLuo.Modules.Memory.Core.Models.MemoryEmotion.Positive
                : delta < 0
                    ? openLuo.Modules.Memory.Core.Models.MemoryEmotion.Negative
                    : openLuo.Modules.Memory.Core.Models.MemoryEmotion.Neutral,
            Importance = Math.Clamp(0.45f + Math.Abs(delta) * 0.03f, 0.2f, 1.0f),
            Metadata = new Dictionary<string, string>
            {
                ["item_id"] = item.Id,
                ["item_name"] = item.DisplayName,
                ["affection_delta"] = delta.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["current_affection"] = newAffectionText
            }
        }, ct);

        var reply = await GenerateGiftReplyAsync(state, character, item, ct);
        state.LastInteractionDay = state.CurrentDay;
        await stateRepo.SaveAsync(state, ct);

        var affection = int.TryParse(
            newAffectionText,
            out var affectionValue)
            ? affectionValue
            : 0;
        var stageState = await stateQueryService.GetAsync(state.Id, "char_status", StateOwnerKind.Character, character.Id, "relationship_stage");
        var stageStrForReturn = stageState.Value
            ?? RelationshipStageLabel(affection);

        return GiftExecutionView.Ok(
            item.Id,
            item.DisplayName,
            character.Name,
            reply.Trim(),
            delta,
            affection,
            stageStrForReturn);
    }

    private async Task<string> GenerateGiftReplyAsync(GameState state, Character character, ItemDefinition item, CancellationToken ct)
    {
        var moodState = await stateQueryService.GetAsync(state.Id, "char_status", StateOwnerKind.Character, character.Id, "mood");
        var affectionState = await stateQueryService.GetAsync(state.Id, "char_status", StateOwnerKind.Character, character.Id, "affection");
        var mood = moodState.Value ?? "Neutral";
        var affection = affectionState.Value ?? "0";
        var moodLabel = mood switch
        {
            "Happy" => "开心",
            "Neutral" => "平静",
            "Sad" => "难过",
            "Angry" => "生气",
            _ => "平静"
        };
        var stageState = await stateQueryService.GetAsync(state.Id, "char_status", StateOwnerKind.Character, character.Id, "relationship_stage");
        var stageStrForReply = stageState.Value;
        var affectionInt = int.TryParse(affection, out var parsedAffection) ? parsedAffection : 0;
        var stageLabel = !string.IsNullOrEmpty(stageStrForReply) ? stageStrForReply : RelationshipStageLabel(affectionInt);
        var archetype = archetypeCatalog.GetById(character.ArchetypeId);
        var prompt = $"""
            你是 {character.Name}。{archetype?.Prompt ?? ""}
            当前心情：{moodLabel}，与玩家（{state.PlayerName}）的关系：{stageLabel}，好感度：{affection}。
            玩家刚刚送给你「{item.DisplayName}」（{item.Description}）。
            请用一两句话自然地表达你收到礼物时的反应，符合当前心情和关系阶段，不要提及好感度数值。
            """;

        return await LlmCallHelper.CallWithLoggingAsync(llmClient, logger, "gift/execute", prompt);
    }

    private async Task<GameState?> ResolveCurrentStateAsync(CancellationToken ct = default)
    {
        var gameId = gameContextAccessor.Current?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        return await stateRepo.GetAsync(gameId, ct);
    }
}
