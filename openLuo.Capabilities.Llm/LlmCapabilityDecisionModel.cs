using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;

namespace openLuo.Capabilities.Llm;

/// <summary>
/// ICapabilityDecisionModel 的 LLM 实现（D36）。
/// 把 CapabilityDecisionContext 转换为 ILlmClient 调用：SystemBlocks → system 消息、
/// Conversation → 对话消息、Catalog → 原生 tool declarations（ModelToolName）。
/// 输出：无 tool_calls 非空文本 → FinalText（D2）；有 tool_calls → Calls（D17 内部文本丢弃）。
/// </summary>
public sealed class LlmCapabilityDecisionModel : ICapabilityDecisionModel
{
    private readonly ILlmClient _llmClient;
    private readonly LlmOptionsFactory _optionsFactory;

    public LlmCapabilityDecisionModel(ILlmClient llmClient, LlmOptionsFactory? optionsFactory = null)
    {
        _llmClient = llmClient;
        _optionsFactory = optionsFactory ?? LlmOptionsFactory.Default;
    }

    public async Task<CapabilityDecision> DecideAsync(
        CapabilityDecisionContext context,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            // 内核协议说明（固定首条，D29）：结构/增强块格式/工具规则/输出约定
            new(ChatMessageRole.System, KernelPrompt.Content)
        };

        // SystemBlocks（增强上下文）作为 system 消息注入（统一经 Contributor 管线组装，
        // 格式 [TAG]\n内容\n[/TAG]）。[Capabilities] 块除外：目录已作为原生 tools
        // 参数下发（下方 options.Tools），文本块重复且是模型文本模仿工具调用的温床。
        foreach (var block in context.SystemBlocks)
        {
            if (!string.IsNullOrWhiteSpace(block) && !block.Contains("[Capabilities]", StringComparison.Ordinal))
                messages.Add(new ChatMessage(ChatMessageRole.System, block));
        }

        // Conversation 对话消息（用户消息携带多模态块：图片经 DataUri 送视觉模型）
        foreach (var message in context.Conversation)
        {
            messages.Add(message.Role switch
            {
                "assistant" => BuildAssistantMessage(message),
                "tool" => new ChatMessage(ChatMessageRole.Tool, message.Content) { ToolCallId = message.ToolCallId },
                "system" => new ChatMessage(ChatMessageRole.System, message.Content),
                _ => new ChatMessage(ChatMessageRole.User, message.Content) { Blocks = message.Blocks?.OfType<Block>().ToArray() }
            });
        }

        // 当前用户输入（含图时图片块随消息送视觉模型）
        if (!string.IsNullOrWhiteSpace(context.UserInput) || context.UserBlocks is { Count: > 0 })
            messages.Add(new ChatMessage(ChatMessageRole.User, context.UserInput ?? string.Empty)
            {
                Blocks = context.UserBlocks?.OfType<Block>().ToArray()
            });
        // 能力目录 → 原生 tool declarations（含真实 JSON Schema 参数，模型才能正确传参）
        var tools = context.Capabilities
            .Where(c => !string.IsNullOrWhiteSpace(c.ModelToolName))
            .Select(c => new LlmToolSpec
            {
                Name = c.ModelToolName,
                Description = string.IsNullOrWhiteSpace(c.Usage)
                    ? c.Summary
                    : $"{c.Summary} 使用时机：{c.Usage}",
                Parameters = c.InputSchema switch
                {
                    JsonObject obj => obj,
                    JsonNode node => node.AsObject(),
                    null => null,
                    var other => JsonSerializer.SerializeToNode(other)?.AsObject()
                }
            })
            .ToList();

        var options = _optionsFactory.Build(context);
        options.Tools = tools;
        var response = await _llmClient.CompleteAsync(messages, options, ct);

        if (response.ToolCalls.Count == 0)
        {
            // D2：无 tool_call 的非空文本 = 最终回复（Respond）
            return new CapabilityDecision
            {
                Messages = string.IsNullOrWhiteSpace(response.Content)
                    ? []
                    : [new FlowItem { Mode = FlowMode.Respond, Kind = ReplyItemKind.Text, Payload = response.Content }]
            };
        }

        // D17：伴随 tool_call 的文本 = 中途消息（Inqueue，群聊即时反馈；不结束回合）
        var calls = response.ToolCalls
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new CapabilityCall
            {
                InvocationId = Guid.NewGuid().ToString("N"),
                IdempotencyKey = string.Empty,   // 决策循环统一生成
                ModelCallId = string.IsNullOrWhiteSpace(t.Id) ? null : t.Id,
                ModelToolName = t.Name,
                RawArgumentsJson = t.ArgumentsJson,
                CanonicalId = ResolveCanonicalId(t.Name, context),
                Args = ParseArgs(t.ArgumentsJson),
                Options = ParseOptions(t.ArgumentsJson)
            })
            .ToList();

        return new CapabilityDecision
        {
            Calls = calls,
            Messages = string.IsNullOrWhiteSpace(response.Content)
                ? []
                : [new FlowItem { Mode = FlowMode.Inqueue, Kind = ReplyItemKind.Text, Payload = response.Content }]
        };
    }

    private static string ResolveCanonicalId(string modelToolName, CapabilityDecisionContext context)
    {
        // ModelToolName 由目录快照生成，映射在 CapabilitySummary 中；无法解析时回退原名
        var summary = context.Capabilities.FirstOrDefault(c =>
            string.Equals(c.ModelToolName, modelToolName, StringComparison.OrdinalIgnoreCase));
        if (summary is not null)
            return summary.CanonicalId;

        // 容错：模型在长工具列表中偶尔混淆名称前缀（实测 flash 把
        // maps_direction_driving 记成 maps_maps_direction_driving）。确定性哈希后缀
        // 来自 canonicalId（GenerateModelToolName 的 _suffix），按后缀匹配可恢复。
        var separator = modelToolName.LastIndexOf('_');
        if (separator > 0 && separator < modelToolName.Length - 1)
        {
            var suffix = modelToolName[(separator + 1)..];
            var fuzzy = context.Capabilities.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.ModelToolName)
                && c.ModelToolName.EndsWith($"_{suffix}", StringComparison.OrdinalIgnoreCase));
            if (fuzzy is not null)
                return fuzzy.CanonicalId;
        }

        return modelToolName;
    }

    /// <summary>构建 assistant 消息；带 tool_calls 声明 JSON 时反序列化为原生 tool_calls（D17 协议）。</summary>
    private static ChatMessage BuildAssistantMessage(ContextMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.ToolCallsJson))
            return new ChatMessage(ChatMessageRole.Assistant, message.Content);

        try
        {
            var calls = JsonSerializer.Deserialize<List<LlmToolCall>>(message.ToolCallsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new ChatMessage(ChatMessageRole.Assistant, message.Content) { ToolCalls = calls };
        }
        catch
        {
            return new ChatMessage(ChatMessageRole.Assistant, message.Content);
        }
    }

    private static string[] ParseArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return [];

        try
        {
            var json = JsonNode.Parse(argumentsJson)?.AsObject();
            return json?["args"]?.AsArray()
                .Select(node => node?.GetValue<string>() ?? string.Empty)
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> ParseOptions(string argumentsJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return result;

        try
        {
            var json = JsonNode.Parse(argumentsJson)?.AsObject();
            if (json?["options"]?.AsObject() is { } options)
            {
                foreach (var pair in options)
                    result[pair.Key] = pair.Value?.GetValue<string>() ?? string.Empty;
            }
        }
        catch
        {
            // 忽略解析失败
        }

        return result;
    }
}

/// <summary>构建 LlmOptions（温度/token 上限等），宿主可覆盖。</summary>
public class LlmOptionsFactory
{
    public static LlmOptionsFactory Default { get; } = new();

    public virtual LlmOptions Build(CapabilityDecisionContext context) => new()
    {
        Temperature = 0.7f,
        MaxTokens = 2048,
        // 上下文或当前输入含图时自动开启多模态：路由据此要求视觉模型，
        // 且 ImageBlock.DataUri 才会真正序列化为 image_url（LlmClientBase 双重门控）。
        EnableMultimodal = context.UserBlocks?.Any(b => b is ImageBlock) == true
                          || context.Conversation.Any(m => m.Blocks?.Any(b => b is ImageBlock) == true)
    };
}
