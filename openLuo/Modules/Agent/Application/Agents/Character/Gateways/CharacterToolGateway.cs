using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.ToolUse;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterToolGateway : ICharacterToolGateway
{
    private readonly IExecutor<ToolUseInput, ToolUseOutput> _toolUseExecutor;
    private readonly IAgentCapabilityExecutor _capabilityExecutor;
    private readonly IAgentStepHook _stepHook;
    private readonly IGameLogger _logger;
    private readonly IPluginHost? _pluginHost;
    private readonly IRuntimeConfigCenter? _config;

    public CharacterToolGateway(
        IExecutor<ToolUseInput, ToolUseOutput> toolUseExecutor,
        IAgentCapabilityExecutor capabilityExecutor,
        IAgentStepHook stepHook,
        IGameLogger logger,
        IPluginHost? pluginHost = null,
        IRuntimeConfigCenter? config = null)
    {
        _toolUseExecutor = toolUseExecutor;
        _capabilityExecutor = capabilityExecutor;
        _stepHook = stepHook;
        _logger = logger;
        _pluginHost = pluginHost;
        _config = config;
    }

    public async Task<CharacterToolCallResult> ExecuteAsync(CharacterTurnContext context, CancellationToken ct = default)
        => await ExecuteAsync(context, new CharacterToolExecutionRequest(), ct);

    public async Task<CharacterToolCallResult> ExecuteAsync(
        CharacterTurnContext context,
        CharacterToolExecutionRequest executionRequest,
        CancellationToken ct = default)
    {
        var request = context.Request;
        var stepContext = new AgentStepContext
        {
            GameId = request.Context.GameId,
            CharacterId = request.Context.CharacterId,
            MessageType = request.Message.Type.ToString(),
            MessagePayload = request.Message.Payload,
            AvailableCapabilities = context.CapabilitySnapshot.Capabilities,
            Profile = request.Profile
        };
        await _stepHook.OnAgentStepBeforeAsync(stepContext, ct);

        var result = await ExecuteToolStageAsync(context, executionRequest, ct);

        await _stepHook.OnAgentStepAfterAsync(new AgentStepAfterContext
        {
            Step = stepContext,
            Result = result
        }, ct);

        _logger.Info("character/turn", $"tool stage completed: steps={result.Steps.Count}, pending={result.PendingAbility is not null}, end={result.EndDialogue}");

        return new CharacterToolCallResult
        {
            Reply = (result.Reply ?? string.Empty).Trim(),
            InterAgentOutcome = result.InterAgentOutcome,
            Steps = result.Steps,
            VisibleBlocks = result.VisibleBlocks,
            Presentation = result.Presentation,
            PendingAbility = result.PendingAbility,
            EndDialogue = result.EndDialogue,
            ShouldContinueToolLoop = result.ShouldContinueToolLoop
        };
    }

    private async Task<AgentToolUseResult> ExecuteToolStageAsync(CharacterTurnContext context, CharacterToolExecutionRequest executionRequest, CancellationToken ct)
    {
        var candidateTools = ResolveCandidateTools(context, executionRequest);
        if (candidateTools.Count == 0)
            return new AgentToolUseResult
            {
                Steps =
                [
                    new AgentToolUseStep
                    {
                        Iteration = executionRequest.Iteration <= 0 ? 1 : executionRequest.Iteration,
                        Action = "tool_use",
                        Name = "none",
                        Success = false,
                        Summary = executionRequest.AllowedToolNames.Count > 0
                            ? $"requested tools not found: {string.Join(", ", executionRequest.AllowedToolNames)}"
                            : "no candidate tools available"
                    }
                ]
            };

        var executors = _config?.GetSnapshot().Executors;
        var toolDecision = await _toolUseExecutor.ExecuteAsync(new ToolUseInput
        {
            Temperature = executors?.ToolUse?.Temperature,
            MaxTokens = executors?.ToolUse?.MaxTokens,
            CharacterProfile = context.PromptContext.CharacterProfile,
            SceneState = context.PromptContext.SceneState,
            CurrentGoal = context.PromptContext.GoalContext,
            PlanSummary = "plan=<deprecated>",
            AvailableTools = candidateTools.Select(MapCapability).ToList(),
            Conversation = context.PromptContext.Conversation.Select(message => $"{message.Role}: {message.Content}").ToList(),
            PlayerInput = context.PromptContext.PlayerInput,
            LastToolResult = !string.IsNullOrWhiteSpace(executionRequest.LastToolResult)
                ? executionRequest.LastToolResult
                : context.Request.Message.Type == AgentMessageType.ToolResult ? context.Request.Message.Payload : string.Empty
        }, ct);

        if (!toolDecision.Success || toolDecision.Output is null)
            return BuildFailedResult("tool_decision", toolDecision.Error ?? "tool use executor failed");

        var output = toolDecision.Output;
        if (!string.Equals(output.Action, "call_tool", StringComparison.OrdinalIgnoreCase))
            return new AgentToolUseResult
            {
                Steps =
                [
                    new AgentToolUseStep
                    {
                        Iteration = executionRequest.Iteration <= 0 ? 1 : executionRequest.Iteration,
                        Action = "tool_use",
                        Name = "none",
                        Success = true,
                        Summary = string.IsNullOrWhiteSpace(output.Reason) ? "no tool required" : output.Reason
                    }
                ]
            };

        var capability = candidateTools.FirstOrDefault(tool => string.Equals(tool.Name, output.ToolName, StringComparison.OrdinalIgnoreCase));
        if (capability is null)
            return BuildFailedResult(output.ToolName, "tool use executor selected an unknown tool");

        _logger.Info("character/tool", $"selected tool: {capability.Name}", new
        {
            gameId = context.Request.Context.GameId,
            characterId = context.Request.Context.CharacterId,
            executorKind = capability.ExecutorKind,
            allowedToolNames = GetStepAllowedToolNames(executionRequest, context),
            args = output.Args,
            options = output.Options,
            reason = output.Reason
        });

        if (capability.NeedsConfirm)
        {
            return new AgentToolUseResult
            {
                PendingAbility = new AgentPendingAbility
                {
                    Capability = capability,
                    Args = output.Args,
                    Options = output.Options
                },
                Steps =
                [
                    new AgentToolUseStep
                    {
                        Iteration = executionRequest.Iteration <= 0 ? 1 : executionRequest.Iteration,
                        Action = "call_tool",
                        Name = capability.Name,
                        Success = false,
                        Summary = $"能力需要确认：{capability.Name}（risk={capability.RiskLevel}）"
                    }
                ]
            };
        }

        var execution = await _capabilityExecutor.ExecuteAsync(
            capability,
            output.Args,
            output.Options,
            new AgentCapabilityContext
            {
                GameId = context.Request.Context.GameId,
                CharacterId = context.Request.Context.CharacterId,
                ExecutionContext = context.Request.ExecutionContext
            },
            ct);

        _logger.Info("character/tool", $"executed tool: {capability.Name} [{(execution.Success ? "ok" : "fail")}]", new
        {
            gameId = context.Request.Context.GameId,
            characterId = context.Request.Context.CharacterId,
            output = execution.Output,
            error = execution.Error
        });

        var pluginBlocks = await CallToolExecutedHookAsync(context, capability, output, execution, ct);

        return new AgentToolUseResult
        {
            Reply = FormatCommandResult(execution),
            InterAgentOutcome = TryGetInterAgentOutcome(execution),
            VisibleBlocks = string.IsNullOrWhiteSpace(execution.Output)
                ? [.. pluginBlocks]
                : [execution.Output.Trim(), .. pluginBlocks],
            Presentation = execution.Presentation,
            Steps =
            [
                new AgentToolUseStep
                {
                    Iteration = executionRequest.Iteration <= 0 ? 1 : executionRequest.Iteration,
                    Action = "call_tool",
                    Name = capability.Name,
                    Success = execution.Success,
                    Summary = execution.Success
                        ? Truncate(execution.Output)
                        : Truncate(execution.Error ?? execution.Output)
                }
            ],
            ShouldContinueToolLoop = false
        };
    }

    private async Task<IReadOnlyList<string>> CallToolExecutedHookAsync(
        CharacterTurnContext context,
        AgentCapabilityDescriptor capability,
        ToolUseOutput decision,
        CommandResult execution,
        CancellationToken ct)
    {
        if (_pluginHost is null)
            return [];

        try
        {
            var assetBlocks = execution.Presentation.Messages
                .SelectMany(x => x.Blocks)
                .Where(b => b is ImageBlock or AssetBlock)
                .ToList();

            var result = await _pluginHost.CallToolExecutedHookAsync(new OnToolExecutedInput
            {
                GameId = context.Request.Context.GameId,
                CharacterId = context.Request.Context.CharacterId,
                ArchetypeId = context.Request.Profile.ArchetypeId,
                ToolName = capability.Name,
                ExecutorKind = capability.ExecutorKind,
                Success = execution.Success,
                Args = decision.Args,
                Options = decision.Options,
                OutputText = execution.Output,
                ErrorText = execution.Error,
                AssetIds = assetBlocks.Select(b => b switch { ImageBlock img => img.AssetId, AssetBlock a => a.AssetId, _ => "" })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                MimeTypes = assetBlocks.Select(b => b switch { ImageBlock img => img.MimeType, AssetBlock a => a.MimeType, _ => "" })
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
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
            _logger.Warn("character/tool", $"plugin onToolExecuted skipped: {ex.Message}");
            return [];
        }
    }

    private IReadOnlyList<AgentCapabilityDescriptor> ResolveCandidateTools(CharacterTurnContext context, CharacterToolExecutionRequest executionRequest)
    {
        var candidates = executionRequest.AllowedToolNames.Count > 0
            ? executionRequest.AllowedToolNames.ToArray()
            : [];
        if (candidates.Length == 0)
            return context.CapabilitySnapshot.Capabilities;

        var set = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return context.CapabilitySnapshot.Capabilities
            .Where(tool => set.Contains(tool.Name) || tool.Aliases.Any(set.Contains))
            .ToList();
    }

    private static ToolUseCapability MapCapability(AgentCapabilityDescriptor capability) => new()
    {
        Name = capability.Name,
        Help = capability.HelpShort,
        Usage = capability.Usage,
        RiskLevel = capability.RiskLevel,
        NeedsConfirm = capability.NeedsConfirm
    };

    private static IReadOnlyList<string> GetStepAllowedToolNames(
        CharacterToolExecutionRequest executionRequest,
        CharacterTurnContext context)
    {
        if (executionRequest.AllowedToolNames.Count > 0)
            return executionRequest.AllowedToolNames;
        return [];
    }

    private static AgentToolUseResult BuildFailedResult(string name, string summary) => new()
    {
        Steps =
        [
            new AgentToolUseStep
            {
                Iteration = 1,
                Action = "tool_use",
                Name = name,
                Success = false,
                Summary = summary
            }
        ]
    };

    private static string FormatCommandResult(CommandResult result)
    {
        var body = result.Success ? result.Output : result.Error ?? result.Output;
        return string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var text = value.Trim();
        return text.Length <= 160 ? text : text[..160] + "...";
    }

    private static InterAgentOutcome? TryGetInterAgentOutcome(CommandResult execution)
    {
        if (execution.Metadata.TryGetValue(CommandResultMetadataKeys.InterAgentOutcome, out var obj) &&
            obj is InterAgentOutcome outcome)
            return outcome;
        return null;
    }

    public async Task<CharacterToolCallResult> ExecuteCapabilityDirectlyAsync(
        CharacterTurnContext context,
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        CancellationToken ct = default)
    {
        var execution = await _capabilityExecutor.ExecuteAsync(
            capability,
            args,
            options,
            new AgentCapabilityContext
            {
                GameId = context.Request.Context.GameId,
                CharacterId = context.Request.Context.CharacterId,
                ExecutionContext = context.Request.ExecutionContext
            },
            ct);

        _logger.Info("character/tool", $"executed tool (direct): {capability.Name} [{(execution.Success ? "ok" : "fail")}]", new
        {
            gameId = context.Request.Context.GameId,
            characterId = context.Request.Context.CharacterId,
            output = execution.Output,
            error = execution.Error
        });

        var pluginBlocks = await CallToolExecutedHookAsync(context, capability, new ToolUseOutput
        {
            Action = "call_tool",
            ToolName = capability.Name,
            Args = args,
            Options = options
        }, execution, ct);

        return new CharacterToolCallResult
        {
            Reply = FormatCommandResult(execution),
            InterAgentOutcome = TryGetInterAgentOutcome(execution),
            VisibleBlocks = string.IsNullOrWhiteSpace(execution.Output)
                ? [.. pluginBlocks]
                : [execution.Output.Trim(), .. pluginBlocks],
            Presentation = execution.Presentation,
            Steps =
            [
                new AgentToolUseStep
                {
                    Iteration = 1,
                    Action = "call_tool",
                    Name = capability.Name,
                    Success = execution.Success,
                    Summary = execution.Success
                        ? Truncate(execution.Output)
                        : Truncate(execution.Error ?? execution.Output)
                }
            ]
        };
    }
}
