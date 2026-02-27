using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.RandomImage;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.InterAgent.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.AgentCapabilities.Application;

public sealed partial class UnifiedAgentCapabilityExecutor
{
    // ── Core capability handlers ──────────────────────────────────────

    private async Task<CommandResult> HandleListCharactersAsync(AgentCapabilityContext context, CancellationToken ct)
    {
        var characters = await roster.ListAsync(context.GameId, ct);
        if (characters.Count == 0)
            return CommandResult.Ok("当前没有可联系角色。");

        var lines = new List<string> { "当前可联系角色：" };
        foreach (var character in characters.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            lines.Add($"- {character.Name} ({character.Id}) archetype={character.ArchetypeId}");
        return CommandResult.Ok(string.Join("\n", lines));
    }

    private async Task<CommandResult> HandleNarrativeChatAsync(
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        var message = ParseNarrativeChatMessage(args, options);
        if (string.IsNullOrWhiteSpace(message))
            return CommandResult.Fail("narrative_chat 缺少 message。用法：narrative_chat --message <玩家输入>");

        return await commandBridge.ExecuteAsync(
            "chat",
            [message],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            context.CharacterId,
            new GameBridgeRequestContext
            {
                GameId = context.GameId,
                ActorId = context.CharacterId,
                Reason = "agent/capability:narrative_chat"
            },
            null,
            ct);
    }

    private async Task<CommandResult> HandleOfferGiftAsync(
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        if (giftService is null)
            return CommandResult.Fail("赠礼能力当前不可用。");
        var itemRef = ParseGiftItem(args, options);
        if (string.IsNullOrWhiteSpace(itemRef))
            return CommandResult.Fail("offer_gift 缺少 item。用法：offer_gift --item <物品名>");

        var result = await giftService.ExecuteAsync(itemRef, ct);
        if (!result.Success)
            return CommandResult.Fail(result.Error ?? "赠礼失败。");

        var delta = result.AffectionDelta >= 0 ? $"+{result.AffectionDelta}" : result.AffectionDelta.ToString();
        return CommandResult.Ok(
            $"已接受礼物「{result.ItemName}」。{result.CharacterName}：{result.Reply}\n好感度 {delta}（当前：{result.CurrentAffection}）");
    }

    private async Task<CommandResult> HandleFetchRandomImageAsync(AgentCapabilityContext context, CancellationToken ct)
    {
        var image = await randomImageFetchExecutor.ExecuteAsync(new RandomImageFetchInput
        {
            GameId = context.GameId,
            OwnerCharacterId = context.CharacterId
        }, ct);
        if (!image.Success || image.Output is null)
            return CommandResult.Fail(image.Error ?? "随机图片获取失败。");

        if (!string.IsNullOrWhiteSpace(image.Output.FallbackArtworkUrl))
        {
            var fallbackText = $"\n{image.Output.FallbackArtworkUrl}";
            return CommandResult.Ok(
                new CommandPresentation
                {
                    Messages =
                    [
                        new Message
                        {
                            MessageId = Guid.NewGuid().ToString("N"),
                            SpeakerRole = "assistant",
                            SpeakerId = context.CharacterId,
                            Blocks =
                            [
                                new TextBlock
                                {
                                    Kind = BlockKind.Text,
                                    Text = fallbackText
                                }
                            ]
                        }
                    ]
                });
        }

        if (string.IsNullOrWhiteSpace(image.Output.AssetId) || string.IsNullOrWhiteSpace(image.Output.MimeType))
            return CommandResult.Fail(image.Error ?? "随机图片获取失败。");

        return CommandResult.Ok(
            new CommandPresentation
            {
                Messages =
                [
                    new Message
                    {
                        MessageId = Guid.NewGuid().ToString("N"),
                        SpeakerRole = "assistant",
                        SpeakerId = context.CharacterId,
                        Blocks =
                        [
                            new ImageBlock
                            {
                                Kind = BlockKind.Image,
                                AssetId = image.Output.AssetId,
                                MimeType = image.Output.MimeType,
                                Name = image.Output.Label ?? "随机图片",
                                AltText = "随机图片",
                                Caption = "随机图片",
                                RenderHint = "inline"
                            }
                        ]
                    }
                ]
            });
    }

    // ── Inter-agent capability handlers ───────────────────────────────

    private async Task<CommandResult> HandleAskCharacterAsync(
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        var request = ParseAskCharacterRequest(args, options);
        if (string.IsNullOrWhiteSpace(request.target))
            return CommandResult.Fail("ask_character 缺少目标角色。用法：ask_character --target <角色> --question <问题>");
        if (string.IsNullOrWhiteSpace(request.question))
            return CommandResult.Fail("ask_character 缺少问题内容。用法：ask_character --target <角色> --question <问题>");

        var targetCharacter = await roster.ResolveAsync(context.GameId, request.target, ct);
        if (targetCharacter is null)
            return CommandResult.Fail($"找不到目标角色：{request.target}");

        var result = await interAgentMessenger.AskAsync(
            new InterAgent.Core.Models.InterAgentAskRequest
            {
                GameId = context.GameId,
                FromCharacterId = context.CharacterId,
                TargetSelector = request.target,
                Question = request.question,
                ExecutionContext = context.ExecutionContext
            },
            ct);

        if (!result.Success)
            return CommandResult.Fail(result.Error);

        var commandResult = CommandResult.Ok($"来自 {result.TargetDisplayName} 的回复：\n{result.Reply}");
        if (result.Outcome is not null)
            commandResult.Metadata[CommandResultMetadataKeys.InterAgentOutcome] = result.Outcome;
        return commandResult;
    }

    private async Task<CommandResult> HandleChatSessionAsync(
        string[] args,
        Dictionary<string, string> options,
        AgentCapabilityContext context,
        CancellationToken ct)
    {
        var request = ParseChatSessionRequest(args, options);
        if (string.IsNullOrWhiteSpace(request.target))
            return CommandResult.Fail("chat_with_character_session 缺少目标角色。用法：chat_with_character_session --target <角色> --opening <开场白>");
        if (string.IsNullOrWhiteSpace(request.opening))
            return CommandResult.Fail("chat_with_character_session 缺少开场白。用法：chat_with_character_session --target <角色> --opening <开场白>");

        var result = await interAgentMessenger.ChatSessionAsync(
            new InterAgent.Core.Models.InterAgentChatSessionRequest
            {
                GameId = context.GameId,
                FromCharacterId = context.CharacterId,
                TargetSelector = request.target,
                Opening = request.opening,
                ExecutionContext = context.ExecutionContext
            },
            ct);

        if (!result.Success)
            return CommandResult.Fail(result.Error);

        var transcript = string.Join("\n", result.Transcript.Select(turn => $"{turn.SpeakerDisplayName}：{turn.Content}"));
        return CommandResult.Ok(transcript);
    }
}
