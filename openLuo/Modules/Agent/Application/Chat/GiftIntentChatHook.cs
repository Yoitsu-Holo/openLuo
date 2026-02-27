using openLuo.Core.Interfaces;
using openLuo.Modules.Content.Application;
using openLuo.Modules.Executor.Application.GiftIntent;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Gameplay.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class GiftIntentChatHook : IAgentChatHookStage
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ItemDefinitionCatalog _itemCatalog;
    private readonly IExecutor<GiftIntentInput, GiftIntentOutput> _giftIntentExecutor;
    private readonly IGameLogger _logger;
    private readonly IRuntimeConfigCenter? _config;

    public GiftIntentChatHook(
        IInventoryRepository inventoryRepository,
        ItemDefinitionCatalog itemCatalog,
        IExecutor<GiftIntentInput, GiftIntentOutput> giftIntentExecutor,
        IGameLogger logger,
        IRuntimeConfigCenter? config = null)
    {
        _inventoryRepository = inventoryRepository;
        _itemCatalog = itemCatalog;
        _giftIntentExecutor = giftIntentExecutor;
        _logger = logger;
        _config = config;
    }

    public async Task<AgentChatTurnBeforeResult> OnChatTurnBeforeAsync(AgentChatTurnContext context, CancellationToken ct = default)
    {
        var inventory = await _inventoryRepository.GetAllAsync(context.GameId, ct);
        if (inventory.Count == 0)
            return new AgentChatTurnBeforeResult();

        var inventoryItems = inventory
            .Where(kv => kv.Value > 0)
            .Select(kv =>
            {
                var item = _itemCatalog.GetById(kv.Key);
                return item is null
                    ? null
                    : new GiftIntentInventoryItem
                    {
                        Id = item.Id,
                        Name = item.DisplayName,
                        Description = item.Description,
                        Quantity = kv.Value
                    };
            })
            .Where(x => x is not null)
            .Cast<GiftIntentInventoryItem>()
            .ToList();

        if (inventoryItems.Count == 0)
            return new AgentChatTurnBeforeResult();

        try
        {
            var result = await _giftIntentExecutor.ExecuteAsync(
                new GiftIntentInput
                {
                    Temperature = _config?.GetSnapshot().Executors.GiftIntent?.Temperature,
                    MaxTokens = _config?.GetSnapshot().Executors.GiftIntent?.MaxTokens,
                    TargetCharacterName = context.TargetCharacter.Name,
                    PlayerInput = context.PlayerMessage,
                    InventoryItems = inventoryItems
                },
                ct);

            var detection = result.Output;
            if (!result.Success || detection is null || !detection.HasGiftIntent)
                return new AgentChatTurnBeforeResult();

            var matched = MatchInventoryItem(detection.ItemRef, inventoryItems);
            if (matched is null)
            {
                return new AgentChatTurnBeforeResult
                {
                    ExtraContexts =
                    [
                        new AgentContextBlock(
                            EnhanceMessageRule.SafetyOrRuntimeRules,
                            $"礼物检测：玩家似乎想赠送礼物（候选：{detection.ItemRef}），但背包中没有找到对应物品。不要假装已经收到礼物；请礼貌指出对方似乎没有真的拿出这件物品，或请对方确认。")
                    ]
                };
            }

            return new AgentChatTurnBeforeResult
            {
                ExtraContexts =
                [
                    new AgentContextBlock(
                        EnhanceMessageRule.SafetyOrRuntimeRules,
                        $"礼物检测：玩家此轮很可能在赠送礼物。候选物品：{matched.Name}（itemId={matched.Id}, quantity={matched.Quantity}）。如果你决定接受礼物，必须优先调用 offer_gift --item {matched.Name}，不要只口头表示已经收下。若你不想接受，也要明确说明理由。")
                ]
            };
        }
        catch (Exception ex)
        {
            _logger.Warn("chat/hook", $"gift intent detection skipped: {ex.Message}");
            return new AgentChatTurnBeforeResult();
        }
    }

    public Task<AgentChatTurnAfterResult> OnChatTurnAfterAsync(AgentChatTurnAfterContext context, CancellationToken ct = default) =>
        Task.FromResult(new AgentChatTurnAfterResult());

    private static GiftIntentInventoryItem? MatchInventoryItem(string? itemRef, IReadOnlyList<GiftIntentInventoryItem> inventoryItems)
    {
        if (string.IsNullOrWhiteSpace(itemRef))
            return null;

        var normalized = itemRef.Trim();
        return inventoryItems.FirstOrDefault(item =>
            string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(item.Name, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
