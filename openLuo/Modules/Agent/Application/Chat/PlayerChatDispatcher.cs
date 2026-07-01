using openLuo.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.AgentCapabilities.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class PlayerChatDispatcher : IPlayerChatDispatcher
{
    private readonly IAgentRuntimeHub _runtimeHub;
    private readonly IAgentCapabilityExecutor _capabilityExecutor;
    private readonly IAgentChatHook _chatHook;
    private readonly ICommandConfirmationService? _confirmationService;
    private readonly IGameLogger? _logger;
    private readonly IRuntimeConfigCenter? _configCenter;
    private readonly ISessionExecutionContextAccessor? _executionContextAccessor;
    private readonly IPluginHost? _pluginHost;

    private TimeSpan ChatAgentStepTimeout => TimeSpan.FromSeconds(Math.Max(1, _configCenter?.GetSnapshot().Agent.ChatRoundTimeoutSeconds ?? 60));
    private int PendingAbilityConfirmTimeoutSeconds => Math.Max(5, _configCenter?.GetSnapshot().Agent.PendingAbilityConfirmTimeoutSeconds ?? 45);

    public PlayerChatDispatcher(
        IAgentRuntimeHub runtimeHub,
        IAgentCapabilityExecutor capabilityExecutor,
        IAgentChatHook chatHook,
        ICommandConfirmationService? confirmationService = null,
        IGameLogger? logger = null,
        IRuntimeConfigCenter? configCenter = null,
        ISessionExecutionContextAccessor? executionContextAccessor = null,
        IPluginHost? pluginHost = null)
    {
        _runtimeHub = runtimeHub;
        _capabilityExecutor = capabilityExecutor;
        _chatHook = chatHook;
        _confirmationService = confirmationService;
        _logger = logger;
        _configCenter = configCenter;
        _executionContextAccessor = executionContextAccessor;
        _pluginHost = pluginHost;
    }

    public bool CanHandle(ParsedCommand command) =>
        command.Kind == InvocationKind.Command &&
        command.Name.Equals("chat", StringComparison.OrdinalIgnoreCase);

    public async Task<CommandResult> ExecuteAsync(AgentInvocationRequest request, CancellationToken ct = default)
    {
        if (request.Parsed.Args.Length == 0)
            return CommandResult.Fail("请输入聊天内容，例：/chat 你好");

        var targetCharacterId = request.ActiveCharacter.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetCharacterId))
            return CommandResult.Fail("找不到目标角色。");

        await _runtimeHub.EnsurePartyStartedAsync(request.State.Id, ct);
        var execCtx = _executionContextAccessor?.Current;
        var playerBlocks = BuildPlayerBlocks(execCtx);
        var turnContext = new AgentChatTurnContext
        {
            GameId = request.State.Id,
            TargetCharacter = request.ActiveCharacter,
            State = request.State,
            PlayerMessage = string.Join(' ', request.Parsed.Args).Trim(),
            CorrelationId = $"chat_{Guid.NewGuid():N}",
            PresentationProfile = execCtx?.PresentationProfile ?? SessionRuntime.Core.Models.SessionPresentationProfile.Default
        };
        var sessionMetadata = BuildSessionMetadata(execCtx);
        var beforeHook = await _chatHook.OnChatTurnBeforeAsync(turnContext, ct);
        var msg = string.IsNullOrWhiteSpace(beforeHook.OverriddenPlayerMessage)
            ? turnContext.PlayerMessage
            : beforeHook.OverriddenPlayerMessage.Trim();
        var correlationId = turnContext.CorrelationId;

        _logger?.Info("agent/chat", "player chat dispatch start", new
        {
            gameId = request.State.Id,
            correlationId,
            targetId = targetCharacterId,
            payloadLen = msg.Length,
            parts = playerBlocks?.Count ?? 0,
            imageParts = playerBlocks?.OfType<ImageBlock>().Count() ?? 0,
            mimeTypes = playerBlocks?.OfType<ImageBlock>().Select(i => i.MimeType).ToList()
        });

        var loopOutcome = await RunPlayerChatLoopAsync(request, targetCharacterId, msg, playerBlocks, correlationId, beforeHook, sessionMetadata, ct);
        var finalReply = loopOutcome.FinalReplyMessage?.Payload;

        _logger?.Info("agent/chat", "player chat dispatch finished", new
        {
            gameId = request.State.Id,
            correlationId,
            targetId = targetCharacterId,
            rounds = loopOutcome.Rounds,
            traceLines = loopOutcome.TraceLines.Count,
            visibleBlocks = loopOutcome.VisibleBlocks.Count,
            outputBlocks = loopOutcome.OutputBlocks.Count,
            finalReplyLength = finalReply?.Length ?? 0
        });

        if (string.IsNullOrWhiteSpace(finalReply) && loopOutcome.OutputBlocks.Count == 0 && loopOutcome.TraceLines.Count == 0)
            return CommandResult.Fail("角色未返回可展示内容。");

        var traceBlock = ShouldShowChatTrace(request.Parsed.Options)
            ? FormatTraceBlock(loopOutcome.TraceLines)
            : null;
        var finalOutput = CombineBlocks(
            [traceBlock, .. beforeHook.VisibleBlocks, .. loopOutcome.VisibleBlocks, .. loopOutcome.OutputBlocks, finalReply]);
        var afterHook = await _chatHook.OnChatTurnAfterAsync(new AgentChatTurnAfterContext
        {
            Turn = turnContext,
            FinalReply = finalReply ?? string.Empty,
            VisibleBlocks = [.. beforeHook.VisibleBlocks, .. loopOutcome.VisibleBlocks, .. loopOutcome.OutputBlocks],
            TraceLines = loopOutcome.TraceLines
        }, ct);
        var pluginAfterBlocks = await CallPluginChatAfterAsync(
            turnContext,
            finalReply ?? string.Empty,
            [.. beforeHook.VisibleBlocks, .. loopOutcome.VisibleBlocks],
            loopOutcome.OutputBlocks,
            loopOutcome.TraceLines,
            ct);
        finalOutput = CombineBlocks([finalOutput, .. afterHook.VisibleBlocks, .. pluginAfterBlocks]);
        var commandResult = CommandResult.Ok(
            BuildPresentation(
                targetCharacterId,
                loopOutcome.FinalReplyMessage?.Presentation,
                traceBlock,
                beforeHook.VisibleBlocks,
                loopOutcome.VisibleBlocks,
                loopOutcome.OutputBlocks,
                afterHook.VisibleBlocks,
                pluginAfterBlocks,
                finalReply,
                finalOutput));
        if (loopOutcome.FinalReplyMessage?.Metadata?.TryGetValue("streamedPublicOutput", out var streamed) == true &&
            bool.TryParse(streamed, out var streamedPublicOutput) &&
            streamedPublicOutput)
            commandResult.Metadata[CommandResultMetadataKeys.StreamedPublicOutput] = true;
        return commandResult;
    }

    private async Task<IReadOnlyList<string>> CallPluginChatAfterAsync(
        AgentChatTurnContext turnContext,
        string finalReply,
        IReadOnlyList<string> visibleBlocks,
        IReadOnlyList<string> outputBlocks,
        IReadOnlyList<string> traceLines,
        CancellationToken ct)
    {
        if (_pluginHost is null)
            return [];

        try
        {
            var result = await _pluginHost.CallChatAfterHookAsync(new OnChatAfterInput
            {
                GameId = turnContext.GameId,
                CharacterId = turnContext.TargetCharacter.Id,
                ArchetypeId = turnContext.TargetCharacter.ArchetypeId,
                PlayerMessage = turnContext.PlayerMessage,
                FinalReply = finalReply,
                VisibleBlocks = visibleBlocks,
                OutputBlocks = outputBlocks,
                TraceLines = traceLines,
                PresentationProfile = turnContext.PresentationProfile.ToString()
            }, ct);

            var blocks = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.AdditionalText))
                blocks.Add(result.AdditionalText.Trim());
            if (result.Notices is { Count: > 0 })
                blocks.AddRange(result.Notices.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            return blocks;
        }
        catch (Exception ex)
        {
            _logger?.Warn("agent/chat", $"plugin onChatAfter skipped: {ex.Message}");
            return [];
        }
    }

    private async Task<PlayerChatLoopOutcome> RunPlayerChatLoopAsync(
        AgentInvocationRequest request,
        string targetCharacterId,
        string initialPayload,
        IReadOnlyList<Block>? playerBlocks,
        string correlationId,
        AgentChatTurnBeforeResult beforeHook,
        IReadOnlyDictionary<string, string>? sessionMetadata,
        CancellationToken ct)
    {
        var outcome = new PlayerChatLoopOutcome();
        var currentType = AgentMessageType.Chat;
        var currentFrom = "player";
        var currentPayload = initialPayload;
        var executionContext = new AgentExecutionContext(
            conversationId: correlationId,
            startedAtUtc: DateTimeOffset.UtcNow,
            overallDeadlineUtc: DateTimeOffset.UtcNow.AddSeconds(Math.Max(
                _configCenter?.GetSnapshot().Timeouts.ChatTimeoutSeconds ?? 120,
                _configCenter?.GetSnapshot().Agent.ChatRoundTimeoutSeconds ?? 60)),
            stepIdleTimeout: ChatAgentStepTimeout);

        for (var round = 1; ; round++)
        {
            outcome.Rounds = round;
            executionContext.ReportProgress($"player_chat_round_start:{round}");
            var agentReply = await _runtimeHub.RequestAsync(
                characterId: targetCharacterId,
                type: currentType,
                from: currentFrom,
                payload: currentPayload,
                gameId: request.State.Id,
                correlationId: correlationId,
                timeout: ChatAgentStepTimeout,
                executionContext: executionContext,
                contextBlocks: round == 1 ? beforeHook.ExtraContexts : null,
                blocks: round == 1 ? playerBlocks : null,
                metadata: sessionMetadata,
                ct: ct);

            if (agentReply is null)
            {
                outcome.OutputBlocks.Add("角色回复超时，已停止。");
                return outcome;
            }

            if (agentReply.TraceLines is { Count: > 0 })
                outcome.TraceLines.AddRange(agentReply.TraceLines);
            if (agentReply.VisibleBlocks is { Count: > 0 })
                outcome.VisibleBlocks.AddRange(agentReply.VisibleBlocks);

            if (agentReply.PendingAbility is null)
            {
                outcome.FinalReplyMessage = agentReply;
                return outcome;
            }

            var pending = agentReply.PendingAbility;
            var pendingDisplay = BuildPendingDisplay(pending);
            var pendingLine = $"待确认执行：{pendingDisplay}";

            if (_confirmationService is null)
            {
                outcome.OutputBlocks.Add(pendingLine);
                outcome.OutputBlocks.Add("当前环境不支持交互确认，已停止执行。");
                return outcome;
            }

            var confirmed = await _confirmationService.ConfirmAsync(
                pendingDisplay,
                pending.Capability.RiskLevel,
                timeoutSeconds: PendingAbilityConfirmTimeoutSeconds,
                ct: ct);

            if (!confirmed)
            {
                outcome.OutputBlocks.Add(pendingLine);
                outcome.OutputBlocks.Add("已取消执行。");
                return outcome;
            }

            var executionResult = await _capabilityExecutor.ExecuteAsync(
                pending.Capability,
                pending.Args,
                pending.Options,
                new AgentCapabilityContext
                {
                    GameId = request.State.Id,
                    CharacterId = agentReply.From,
                    ExecutionContext = executionContext
                },
                ct);

            executionContext.ReportProgress($"player_chat_pending_ability_done:{pending.Capability.Name}");
            outcome.OutputBlocks.Add(FormatAbilityExecutionBlock(pendingLine, executionResult));

            currentType = AgentMessageType.ToolResult;
            currentFrom = "host";
            currentPayload = BuildToolResultContinuationPayload(pending, executionResult);
        }
    }

    private static bool ShouldShowChatTrace(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("trace", out var value))
            return false;
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatTraceBlock(IReadOnlyList<string>? traceLines)
    {
        if (traceLines is not { Count: > 0 })
            return null;
        var lines = new List<string> { "Agent 执行：" };
        lines.AddRange(traceLines.Select(x => $"- {x}"));
        return string.Join("\n", lines);
    }

    private static string CombineBlocks(IEnumerable<string?> blocks) =>
        string.Join("\n", blocks.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string BuildPendingDisplay(AgentPendingAbility pending)
    {
        var prefix = string.IsNullOrWhiteSpace(pending.Capability.Prefix) ? string.Empty : pending.Capability.Prefix;
        var parts = new List<string> { $"{prefix}{pending.Capability.Name}" };
        parts.AddRange(pending.Args.Where(a => !string.IsNullOrWhiteSpace(a)));
        foreach (var (key, value) in pending.Options)
        {
            parts.Add($"--{key}");
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                parts.Add(value);
        }
        return string.Join(" ", parts);
    }

    private static string FormatAbilityExecutionBlock(string pendingLine, CommandResult result) =>
        result.Success
            ? CombineBlocks([pendingLine, result.Output])
            : CombineBlocks([pendingLine, result.Error ?? result.Output]);

    private static string BuildToolResultContinuationPayload(AgentPendingAbility pending, CommandResult result)
    {
        var lines = new List<string>
        {
            "这是上一轮能力调用的执行结果。",
            $"能力：{pending.Capability.Name}",
            $"调用：{BuildPendingDisplay(pending)}",
            $"状态：{(result.Success ? "success" : "failed")}"
        };

        var body = result.Success ? result.Output : result.Error ?? result.Output;
        if (!string.IsNullOrWhiteSpace(body))
        {
            lines.Add("结果：");
            lines.Add(body.Trim());
        }

        lines.Add("请继续当前任务：如果已经满足用户目标，就 respond；否则继续规划并调用下一步能力。");
        return string.Join("\n", lines);
    }

    private sealed class PlayerChatLoopOutcome
    {
        public AgentMessage? FinalReplyMessage { get; set; }
        public int Rounds { get; set; }
        public List<string> TraceLines { get; } = [];
        public List<string> VisibleBlocks { get; } = [];
        public List<string> OutputBlocks { get; } = [];
    }

    private static CommandPresentation BuildPresentation(
        string targetCharacterId,
        CommandPresentation? finalPresentation,
        string? traceBlock,
        IReadOnlyList<string> beforeBlocks,
        IReadOnlyList<string> visibleBlocks,
        IReadOnlyList<string> outputBlocks,
        IReadOnlyList<string> afterBlocks,
        IReadOnlyList<string> pluginAfterBlocks,
        string? finalReply,
        string finalOutput)
    {
        if (finalPresentation is { Messages.Count: > 0 })
        {
            var prefixTexts = new List<string?>();
            prefixTexts.Add(traceBlock);
            prefixTexts.AddRange(beforeBlocks);
            prefixTexts.AddRange(visibleBlocks);
            prefixTexts.AddRange(outputBlocks);
            prefixTexts.AddRange(afterBlocks);
            prefixTexts.AddRange(pluginAfterBlocks);

            var prefixParts = prefixTexts
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(text => (Block)new TextBlock
                {
                    Kind = BlockKind.Text,
                    Visibility = ResolveVisibility(text!),
                    Text = text!.Trim()
                })
                .ToList();

            if (prefixParts.Count == 0)
                return finalPresentation;

            var messages = new List<Message>
            {
                new()
                {
                    MessageId = Guid.NewGuid().ToString("N"),
                    SpeakerRole = "character",
                    SpeakerId = targetCharacterId,
                    Blocks = prefixParts
                }
            };
            messages.AddRange(finalPresentation.Messages);
            return new CommandPresentation { Messages = messages };
        }

        var standaloneTexts = new List<string?>();
        standaloneTexts.Add(traceBlock);
        standaloneTexts.AddRange(beforeBlocks);
        standaloneTexts.AddRange(visibleBlocks);
        standaloneTexts.AddRange(outputBlocks);
        if (!string.IsNullOrWhiteSpace(finalReply))
            standaloneTexts.Add(finalReply);
        standaloneTexts.AddRange(afterBlocks);
        standaloneTexts.AddRange(pluginAfterBlocks);

        var standaloneParts = standaloneTexts
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(text => (Block)new TextBlock
            {
                Kind = BlockKind.Text,
                Visibility = ResolveVisibility(text!),
                Text = text!.Trim()
            })
            .ToList();

        if (standaloneParts.Count > 0)
        {
            return new CommandPresentation
            {
                Messages =
                [
                    new Message
                    {
                        MessageId = Guid.NewGuid().ToString("N"),
                        SpeakerRole = "character",
                        SpeakerId = targetCharacterId,
                        Visibility = standaloneParts.All(p => p.Visibility == OutputVisibility.StateSummary)
                            ? OutputVisibility.StateSummary
                            : OutputVisibility.Public,
                        Blocks = standaloneParts
                    }
                ]
            };
        }

        return CommandPresentation.FromText(
            string.IsNullOrWhiteSpace(finalOutput) ? finalReply ?? string.Empty : finalOutput,
            speakerRole: "character",
            speakerId: targetCharacterId);
    }

    private static OutputVisibility ResolveVisibility(string text)
    {
        if (text.StartsWith("[状态结算]", StringComparison.Ordinal))
            return OutputVisibility.StateSummary;

        if (text.StartsWith("Agent 执行：", StringComparison.Ordinal))
            return OutputVisibility.Debug;

        return OutputVisibility.Public;
    }

    private static IReadOnlyDictionary<string, string>? BuildSessionMetadata(SessionExecutionContext? execCtx)
    {
        if (execCtx is null)
            return null;

        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(execCtx.SessionId))
            meta["sessionId"] = execCtx.SessionId;
        if (!string.IsNullOrWhiteSpace(execCtx.ChannelId))
            meta["channelId"] = execCtx.ChannelId;
        meta["presentationProfile"] = execCtx.PresentationProfile.ToString();
        return meta.Count > 0 ? meta : null;
    }

    private static IReadOnlyList<Block>? BuildPlayerBlocks(SessionExecutionContext? execCtx)
    {
        if (execCtx is null || execCtx.Attachments is not { Count: > 0 })
            return null;

        var blocks = new List<Block>();
        foreach (var attachment in execCtx.Attachments)
        {
            var mediaType = attachment.MediaType ?? string.Empty;
            if (string.IsNullOrWhiteSpace(attachment.AssetId))
                continue;

            if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new ImageBlock
                {
                    Kind = BlockKind.Image,
                    AssetId = attachment.AssetId,
                    MimeType = mediaType,
                    Name = attachment.Name,
                    AltText = "[用户发送的图片]"
                });
            }
            else
            {
                blocks.Add(new AssetBlock
                {
                    Kind = BlockKind.Asset,
                    AssetId = attachment.AssetId,
                    MimeType = mediaType,
                    Name = attachment.Name
                });
            }
        }

        return blocks.Count > 0 ? blocks : null;
    }
}
