using System.Text;
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

        // 目录块（D12/D13）：技能 / 工作流 / 远程 Agent 的摘要目录。
        // 只渲染摘要，正文与编排留在能力里按需触发——每轮塞全文既吃掉上下文，
        // 又诱导模型照抄正文当回复。空集合不产出块（不塞空标题）。
        foreach (var catalogBlock in BuildCatalogBlocks(context))
            messages.Add(new ChatMessage(ChatMessageRole.System, catalogBlock));

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

    /// <summary>技能正文加载能力（canonical id，D12）。</summary>
    private const string SkillLoadCapabilityId = "core:load_skill";
    private const string SkillUnloadCapabilityId = "core:unload_skill";

    /// <summary>工作流执行能力（canonical id，D13）。</summary>
    private const string WorkflowRunCapabilityId = "core:run_workflow";

    /// <summary>
    /// 目录块（D12/D13）：技能 / 工作流 / 远程 Agent 的摘要目录，随 system 消息下发。
    /// 空集合不出块：模型只在确有候选时才该知道“能加载/能编排”，
    /// 每轮塞一个空目录既浪费上下文也稀释真正的指令。
    /// </summary>
    private static IEnumerable<string> BuildCatalogBlocks(CapabilityDecisionContext context)
    {
        if (context.Skills.Count > 0)
            yield return BuildSkillBlock(context);
        if (context.Workflows.Count > 0)
            yield return BuildWorkflowBlock(context);
        if (context.RemoteAgents.Count > 0)
            yield return BuildRemoteAgentBlock(context);
    }

    private static string BuildSkillBlock(CapabilityDecisionContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Skills]");
        builder.AppendLine("下列技能是可加载的操作指南；这里只有摘要。加载只登记引用——正文会在后续请求的 [LoadedSkills] 块里自动出现，不要重复加载。");
        foreach (var skill in context.Skills)
        {
            builder.Append("- ").Append(SingleLine(skill.Id)).Append(": ").Append(SingleLine(skill.Title));
            // WhenToUse 缺失时退回 Summary：目录的价值就是让模型知道“什么时候用得上”
            var whenToUse = string.IsNullOrWhiteSpace(skill.WhenToUse) ? skill.Summary : skill.WhenToUse;
            if (!string.IsNullOrWhiteSpace(whenToUse))
                builder.Append("。适用时机：").Append(SingleLine(whenToUse));
            builder.AppendLine();
        }
        builder.Append("需要某个技能时调用工具 ").Append(ToolReference(context, SkillLoadCapabilityId))
            .AppendLine("，参数 {\"id\":\"<skillId>\"}；用完不再需要时调用 ")
            .Append(ToolReference(context, SkillUnloadCapabilityId))
            .AppendLine(" 把它从 [LoadedSkills] 里移除（省预算）。");
        builder.Append("[/Skills]");
        return builder.ToString();
    }

    private static string BuildWorkflowBlock(CapabilityDecisionContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Workflows]");
        builder.AppendLine("下列工作流是封装好的多步编排；执行交给工作流能力，不要自行逐步重演。");
        foreach (var workflow in context.Workflows)
        {
            builder.Append("- ").Append(SingleLine(workflow.Id)).Append(": ").AppendLine(SingleLine(workflow.Description));
        }
        builder.Append("执行时调用工具 ").Append(ToolReference(context, WorkflowRunCapabilityId))
            .AppendLine("，参数 {\"id\":\"<workflowId>\"}；其余输入按该工作流定义的键名平铺传入（扁平标量，别嵌套对象）。");
        builder.Append("[/Workflows]");
        return builder.ToString();
    }

    private static string BuildRemoteAgentBlock(CapabilityDecisionContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[RemoteAgents]");
        builder.AppendLine("下列远程 Agent 可接收委派（id 与能力目录一致，用同名工具转发请求）。");
        foreach (var agent in context.RemoteAgents)
        {
            builder.Append("- ").Append(SingleLine(agent.Id));
            if (!string.IsNullOrWhiteSpace(agent.DisplayName))
                builder.Append(" (").Append(SingleLine(agent.DisplayName)).Append(')');
            var description = string.IsNullOrWhiteSpace(agent.Description) ? string.Empty : "：" + SingleLine(agent.Description);
            builder.AppendLine(description);
        }
        builder.Append("[/RemoteAgents]");
        return builder.ToString();
    }

    /// <summary>
    /// 能力引用：本轮 tools 参数里已有该能力时给真实函数名（内核协议要求模型只能用 tools
    /// 列表里的名字调用），否则退回 canonical id（例如能力未注册或被权限过滤）。
    /// </summary>
    private static string ToolReference(CapabilityDecisionContext context, string canonicalId)
    {
        var modelToolName = context.Capabilities.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.ModelToolName)
            && string.Equals(c.CanonicalId, canonicalId, StringComparison.OrdinalIgnoreCase))?.ModelToolName;
        return string.IsNullOrWhiteSpace(modelToolName) ? canonicalId : $"{modelToolName}（canonical id {canonicalId}）";
    }

    /// <summary>摘要里的换行会把一条目录项拆成多行、看起来像新的指令行；压成单行。</summary>
    private static string SingleLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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
                .Select(ValueToText)
                .Where(text => text is not null)
                .Cast<string>()
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
            if (json is null) return result;
            // 两处来源合并：
            // 1) 约定式包装：{"options":{...}}（宿主代码构造 / 模型按 options 语义生成）
            // 2) 顶层命名属性：模型按 InputSchema 的 properties 生成（如 sticker 的
            //    {"description":"..."}）。此前只解析 options 包装，导致声明了命名参数的
            //    能力（send_sticker 首例）参数永远丢失。
            if (json["options"]?.AsObject() is { } options)
            {
                foreach (var pair in options)
                {
                    var text = ValueToText(pair.Value);
                    if (text is not null)
                        result[pair.Key] = text;
                }
            }
            foreach (var pair in json)
            {
                // 排除元键与已由 options 分支覆盖的键
                if (pair.Key is "args" or "options") continue;
                if (pair.Value is JsonObject or JsonArray) continue;
                var text = ValueToText(pair.Value);
                if (text is not null)
                    result.TryAdd(pair.Key, text);
            }
        }
        catch
        {
            // 忽略解析失败（单参数损坏不拖垮整批解析）
        }

        return result;
    }

    /// <summary>JsonValue → 文本：字符串原样；数字保留原文（27908590 不丢精度）；布尔转小写；
    /// 对象/数组/null 返回 null。旧实现直接 GetValue&lt;string&gt;()，模型按 integer/boolean schema
    /// 传参时会抛异常并被整体吞掉 → 参数静默丢失（share_song 首例）。</summary>
    private static string? ValueToText(JsonNode? node) => node switch
    {
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        },
        _ => null
    };
}

/// <summary>构建 LlmOptions（能力开关等），宿主可覆盖。采样参数默认留空 = 交 route 配置。</summary>
public class LlmOptionsFactory
{
    public static LlmOptionsFactory Default { get; } = new();

    /// <summary>
    /// 只表达“需要什么”，不替用户定采样参数：Temperature/MaxTokens 留空（null）= 交 route 配置
    /// （llm.jsonc）。历史上这里硬编码 0.7f/2048，把 route 里写的 temperature/maxTokens 全部顶掉，
    /// 用户配置形同虚设。
    /// </summary>
    public virtual LlmOptions Build(CapabilityDecisionContext context) => new()
    {
        // 上下文或当前输入含图时自动开启多模态：路由据此要求视觉模型，
        // 且 ImageBlock.DataUri 才会真正序列化为 image_url（LlmClientBase 双重门控）。
        EnableMultimodal = context.UserBlocks?.Any(b => b is ImageBlock) == true
                          || context.Conversation.Any(m => m.Blocks?.Any(b => b is ImageBlock) == true)
    };
}
