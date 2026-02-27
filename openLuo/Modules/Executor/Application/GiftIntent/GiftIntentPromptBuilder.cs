using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Modules.Executor.Application.GiftIntent;

public sealed class GiftIntentPromptBuilder : IExecutorPromptBuilder<GiftIntentInput>
{
    public ExecutorPrompt Build(GiftIntentInput input)
    {
        var inventoryBlock = string.Join("\n", input.InventoryItems.Select(item =>
            $"- {item.Name} (itemId={item.Id}, quantity={item.Quantity}, desc={item.Description})"));

        var prompt =
            $$"""
你是一个礼物意图检测器。请判断玩家这句话是否明确在把背包中的某件物品送给当前角色。

【角色】
{{input.TargetCharacterName}}

【玩家输入】
{{input.PlayerInput}}

【背包物品】
{{inventoryBlock}}

只输出一个合法的 JSON 对象，不要输出 markdown 代码块围栏，不要输出额外解释：
{
  "hasGiftIntent": true,
  "itemRef": "<最可能的物品名或空字符串>",
  "confidence": 0.0,
  "reason": "<简短原因>"
}

约束：
- 只有在玩家明确表达"送给你/给你/这份...给你"等赠送意图时，hasGiftIntent 才为 true
- itemRef 优先填写背包中的物品名
- 如果无法确定，hasGiftIntent=false
- JSON 字符串字段中的正文不要包含双引号 `"`，也不要包含中文引号 `"`
""";

        return new ExecutorPrompt
        {
            Messages = [new ChatMessage(ChatMessageRole.User, prompt)],
            Options = new LlmOptions
            {
                Temperature = input.Temperature,
                MaxTokens = input.MaxTokens,
                JsonMode = true
            }
        };
    }
}
