using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.RandomImage;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.AgentCapabilities.Application;

public sealed class UnifiedAgentCapabilityRegistry(
    IAgentCommandBridge commandBridge,
    IAgentRoster roster) : IAgentCapabilityRegistry
{
    private static readonly IReadOnlyList<AgentCapabilityDescriptor> CoreCapabilities =
    [
        new()
        {
            Name = "list_characters",
            Aliases = ["characters", "list_roles", "list_agents"],
            HelpShort = "列出当前存档中可联系的角色",
            Category = "core",
            Prefix = string.Empty,
            Usage = "list_characters",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["roster-query", "character-query"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "core"
        },
        new()
        {
            Name = "narrative_chat",
            Aliases = ["render_dialogue", "dialogue_chat"],
            HelpShort = "调用叙事聊天渲染链并返回最终角色对白",
            Category = "render",
            Prefix = string.Empty,
            Usage = "narrative_chat --message <玩家输入>",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["narrative", "dialogue", "renderer"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "core"
        },
        new()
        {
            Name = "character_response",
            Aliases = ["reply_to_player", "respond"],
            HelpShort = "生成角色对玩家的自然语言回复，适合在完成工具目标后向玩家汇报或进行纯对话",
            Category = "core",
            Prefix = string.Empty,
            Usage = "character_response",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["dialogue", "reply", "chat"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "core"
        },
        new()
        {
            Name = "offer_gift",
            Aliases = ["accept_gift", "receive_gift"],
            HelpShort = "从玩家背包中执行一次真实赠礼，并返回礼物执行结果",
            Category = "core",
            Prefix = string.Empty,
            Usage = "offer_gift --item <物品名>",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["gift", "inventory", "interaction"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "core"
        },
        new()
        {
            Name = "fetch_random_image",
            Aliases = ["random_image", "get_random_image", "send_image"],
            HelpShort = "获取一张随机图片并返回媒体结果，适合来张图、随机图片、涩图等请求",
            Category = "media",
            Prefix = string.Empty,
            Usage = "fetch_random_image",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["image", "media", "random-image"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "core"
        },
        new()
        {
            Name = "ask_character",
            Aliases = ["consult_character", "query_character"],
            HelpShort = "向另一位角色发送问题并等待其真实回复",
            Category = "inter-agent",
            Prefix = string.Empty,
            Usage = "ask_character --target <角色名|角色ID> --question <问题>",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["inter-agent", "character-query", "dialogue"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "inter-agent"
        },
        new()
        {
            Name = "chat_with_character_session",
            Aliases = ["talk_with_character", "character_chat_session"],
            HelpShort = "让当前角色和另一位角色进行一段自然对话，并返回对话记录",
            Category = "inter-agent",
            Prefix = string.Empty,
            Usage = "chat_with_character_session --target <角色名|角色ID> --opening <开场白>",
            RiskLevel = "low",
            NeedsConfirm = false,
            Capabilities = ["inter-agent", "dialogue", "session"],
            ProviderId = "core_agent_capabilities",
            ExecutorKind = "inter-agent"
        }
    ];

    public async Task<AgentCapabilitySnapshot> BuildSnapshotAsync(AgentCapabilityContext context, CancellationToken ct = default)
    {
        var merged = CoreCapabilities
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var knownCharacters = (await roster.ListAsync(context.GameId, ct))
            .Select(character => new AgentKnownCharacter
            {
                CharacterId = character.Id,
                DisplayName = character.Name,
                ArchetypeId = character.ArchetypeId
            })
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AgentCapabilitySnapshot
        {
            Capabilities = merged,
            KnownCharacters = knownCharacters
        };
    }

}

public sealed partial class UnifiedAgentCapabilityExecutor(
    IAgentCommandBridge commandBridge,
    IAgentRoster roster,
    IInterAgentMessenger interAgentMessenger,
    IExecutor<RandomImageFetchInput, RandomImageFetchOutput> randomImageFetchExecutor,
    openLuo.Modules.Gameplay.Application.Services.GiftService? giftService = null) : IAgentCapabilityExecutor
{
    public Task<CommandResult> ExecuteAsync(
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct = default)
    {
        return capability.ExecutorKind.ToLowerInvariant() switch
        {
            "core" => ExecuteCoreAsync(capability, args, options, context, ct),
            "inter-agent" => ExecuteInterAgentAsync(capability, args, options, context, ct),
            _ => ExecuteViaBridgeAsync(capability, args, options, context, ct)
        };
    }

    // ── Capability dispatch ───────────────────────────────────────────

    private async Task<CommandResult> ExecuteCoreAsync(
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        return capability.Name.ToLowerInvariant() switch
        {
            "list_characters" => await HandleListCharactersAsync(context, ct),
            "narrative_chat" => await HandleNarrativeChatAsync(args, options, context, ct),
            "offer_gift" => await HandleOfferGiftAsync(args, options, context, ct),
            "fetch_random_image" => await HandleFetchRandomImageAsync(context, ct),
            _ => CommandResult.Fail($"未实现的核心能力：{capability.Name}")
        };
    }

    private async Task<CommandResult> ExecuteInterAgentAsync(
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        return capability.Name.ToLowerInvariant() switch
        {
            "ask_character" => await HandleAskCharacterAsync(args, options, context, ct),
            "chat_with_character_session" => await HandleChatSessionAsync(args, options, context, ct),
            _ => CommandResult.Fail($"未实现的 inter-agent 能力：{capability.Name}")
        };
    }

    private async Task<CommandResult> ExecuteViaBridgeAsync(
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        return await commandBridge.ExecuteAsync(
            capability.Name,
            args,
            options,
            context.CharacterId,
            new GameBridgeRequestContext
            {
                GameId = context.GameId,
                ActorId = context.CharacterId,
                Reason = "agent/capability"
            },
            string.Equals(capability.Category, "command", StringComparison.OrdinalIgnoreCase) ? null : capability.Category,
            ct);
    }

    // ── Shared parsing helpers ────────────────────────────────────────

    private static (string target, string question) ParseAskCharacterRequest(string[] args, Dictionary<string, string> options)
    {
        var target = options.TryGetValue("target", out var targetValue)
            ? targetValue
            : args.FirstOrDefault() ?? string.Empty;
        var question = options.TryGetValue("question", out var questionValue)
            ? questionValue
            : args.Length > 1
                ? string.Join(' ', args.Skip(1)).Trim()
                : string.Empty;
        return (target.Trim(), question.Trim());
    }

    private static string ParseNarrativeChatMessage(string[] args, Dictionary<string, string> options)
    {
        if (options.TryGetValue("message", out var messageValue) && !string.IsNullOrWhiteSpace(messageValue))
            return messageValue.Trim();
        return string.Join(' ', args).Trim();
    }

    private static string ParseGiftItem(string[] args, Dictionary<string, string> options)
    {
        if (options.TryGetValue("item", out var itemValue) && !string.IsNullOrWhiteSpace(itemValue))
            return itemValue.Trim();
        return string.Join(' ', args).Trim();
    }

    private static (string target, string opening) ParseChatSessionRequest(string[] args, Dictionary<string, string> options)
    {
        var target = options.TryGetValue("target", out var targetValue)
            ? targetValue
            : args.FirstOrDefault() ?? string.Empty;
        var opening = options.TryGetValue("opening", out var openingValue)
            ? openingValue
            : args.Length > 1
                ? string.Join(' ', args.Skip(1)).Trim()
                : string.Empty;
        return (target.Trim(), opening.Trim());
    }
}
