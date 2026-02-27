using System.Text.Json;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public sealed class AgentInvocationRouter : IAgentInvocationRouter
{
    private readonly IPlayerChatDispatcher _playerChat;
    private readonly IAgentCommandBridge _commandBridge;
    private readonly IMultiCharacterOrchestrator _multiCharacter;
    private readonly IPartyTaskRepository _partyTaskRepo;
    private readonly ICommandConfirmationService? _confirmationService;
    private readonly IRuntimeConfigCenter? _configCenter;
    private readonly ISessionExecutionContextAccessor? _sessionExecutionContextAccessor;

    public AgentInvocationRouter(
        IPlayerChatDispatcher playerChat,
        IAgentCommandBridge commandBridge,
        IMultiCharacterOrchestrator multiCharacter,
        IPartyTaskRepository partyTaskRepo,
        ICommandConfirmationService? confirmationService = null,
        IRuntimeConfigCenter? configCenter = null,
        ISessionExecutionContextAccessor? sessionExecutionContextAccessor = null)
    {
        _playerChat = playerChat;
        _commandBridge = commandBridge;
        _multiCharacter = multiCharacter;
        _partyTaskRepo = partyTaskRepo;
        _confirmationService = confirmationService;
        _configCenter = configCenter;
        _sessionExecutionContextAccessor = sessionExecutionContextAccessor;
    }

    public bool CanHandle(ParsedCommand parsed)
    {
        if (_playerChat.CanHandle(parsed))
            return true;

        if (_multiCharacter.CanHandle(parsed))
            return true;

        var pluginCmds = _commandBridge.GetCommands();
        return pluginCmds.Any(c =>
            MatchesInvocation(c, parsed) &&
            (c.Name.Equals(parsed.Name, StringComparison.OrdinalIgnoreCase) ||
             c.Aliases.Contains(parsed.Name, StringComparer.OrdinalIgnoreCase)));
    }

    public async Task<CommandResult> ExecuteAsync(AgentInvocationRequest request, CancellationToken ct = default)
    {
        var parsed = request.Parsed;
        if (_playerChat.CanHandle(parsed))
            return await _playerChat.ExecuteAsync(request, ct);

        if (_multiCharacter.CanHandle(parsed))
        {
            return await _multiCharacter.ExecuteAsync(new MultiCharacterCommandContext
            {
                Kind = parsed.Kind,
                Prefix = parsed.Prefix,
                RawInput = request.RawInput,
                CommandName = parsed.Name,
                Args = parsed.Args,
                Options = parsed.Options,
                State = request.State,
                ActiveCharacter = request.ActiveCharacter
            }, ct);
        }

        var pluginCmd = _commandBridge.GetCommands().FirstOrDefault(c =>
            MatchesInvocation(c, parsed) &&
            (c.Name.Equals(parsed.Name, StringComparison.OrdinalIgnoreCase) ||
             c.Aliases.Contains(parsed.Name, StringComparer.OrdinalIgnoreCase)));
        if (pluginCmd is null)
            return CommandResult.Fail($"未知指令：{parsed.Prefix}{parsed.Name}。输入 /help 查看所有指令。");

        if (RequiresConfirm(parsed, pluginCmd))
        {
            var confirmed = await ConfirmExecutionAsync(parsed, pluginCmd, ct);
            if (!confirmed)
                return CommandResult.Fail($"已取消执行：{parsed.Prefix}{parsed.Name}（risk={pluginCmd.RiskLevel}）。");
        }

        var invocationTask = await StartInvocationTaskIfNeededAsync(request, pluginCmd, ct);
        var result = await _commandBridge.ExecuteAsync(
            parsed.Name,
            parsed.Args,
            parsed.Options,
            request.ActiveCharacter.Id,
            BuildBridgeContext(request),
            parsed.Kind == InvocationKind.Command ? null : pluginCmd.Category,
            ct);
        if (invocationTask is not null)
            await FinalizeInvocationTaskAsync(invocationTask.Value, parsed, result, ct);
        return result;
    }

    private static bool MatchesInvocation(CommandDescriptor command, ParsedCommand parsed)
    {
        var normalized = (command.Category ?? "command").Trim().ToLowerInvariant();
        return parsed.Kind switch
        {
            InvocationKind.Command => normalized is "" or "command",
            InvocationKind.Skill => normalized == "skill",
            InvocationKind.SubAgent => normalized is "subagent" or "sub_agent" or "sub-agent" or "agent",
            InvocationKind.Tool => normalized == "tool",
            _ => false
        };
    }

    private static bool RequiresConfirm(ParsedCommand parsed, CommandDescriptor pluginCmd)
    {
        if (!pluginCmd.NeedsConfirm)
            return false;
        return !HasExplicitConfirmation(parsed.Options);
    }

    private static bool HasExplicitConfirmation(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("confirm", out var v))
            return false;
        return string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(v, "1", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ConfirmExecutionAsync(ParsedCommand parsed, CommandDescriptor pluginCmd, CancellationToken ct)
    {
        if (HasExplicitConfirmation(parsed.Options))
            return true;
        if (_confirmationService is null)
            return false;
        return await _confirmationService.ConfirmAsync(
            $"{parsed.Prefix}{parsed.Name}",
            pluginCmd.RiskLevel,
            timeoutSeconds: Math.Max(5, _configCenter?.GetSnapshot().Agent.InvocationConfirmTimeoutSeconds ?? 30),
            ct: ct);
    }

    private async Task<(string taskId, string stepId)?> StartInvocationTaskIfNeededAsync(
        AgentInvocationRequest request,
        CommandDescriptor pluginCmd,
        CancellationToken ct)
    {
        var parsed = request.Parsed;
        if (parsed.Kind == InvocationKind.Command)
            return null;

        var now = DateTime.UtcNow;
        var commandName = $"{parsed.Prefix}{parsed.Name}";
        var contextJson = JsonSerializer.Serialize(new
        {
            invocationKind = parsed.Kind.ToString(),
            prefix = parsed.Prefix,
            commandName = parsed.Name,
            displayName = commandName,
            pluginId = pluginCmd.ProviderId,
            category = pluginCmd.Category,
            riskLevel = pluginCmd.RiskLevel,
            needsConfirm = pluginCmd.NeedsConfirm,
            capabilities = pluginCmd.Capabilities,
            options = parsed.Options
        });
        var taskId = await _partyTaskRepo.CreateTaskAsync(
            request.State.Id,
            $"{commandName} {string.Join(' ', parsed.Args)}".Trim(),
            "player",
            contextJson,
            ct);
        var stepId = Guid.NewGuid().ToString("N");
        await _partyTaskRepo.CreateStepsAsync(taskId,
        [
            new PartyTaskStepRecord
            {
                Id = stepId,
                TaskId = taskId,
                StepOrder = 1,
                AssignedCharacterId = request.ActiveCharacter.Id,
                Role = parsed.Kind switch
                {
                    InvocationKind.Skill => "skill",
                    InvocationKind.Tool => "tool",
                    _ => "subagent"
                },
                Instruction = $"{commandName} {string.Join(' ', parsed.Args)}".Trim(),
                ResultJson = "{}",
                Status = "running",
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            }
        ], ct);
        return (taskId, stepId);
    }

    private async Task FinalizeInvocationTaskAsync(
        (string taskId, string stepId) taskRef,
        ParsedCommand parsed,
        CommandResult result,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            success = result.Success,
            output = result.Output,
            error = result.Error,
            prefix = parsed.Prefix,
            name = parsed.Name
        });
        await _partyTaskRepo.UpdateStepResultAsync(
            taskRef.stepId,
            result.Success ? "done" : "failed",
            payload,
            ct);
        await _partyTaskRepo.UpdateTaskStatusAsync(
            taskRef.taskId,
            result.Success ? "done" : "failed",
            ct);

        if (result.Success)
        {
            result.Output = string.IsNullOrWhiteSpace(result.Output)
                ? $"任务ID: {taskRef.taskId}"
                : $"{result.Output}\n任务ID: {taskRef.taskId}";
        }
        else
        {
            result.Error = string.IsNullOrWhiteSpace(result.Error)
                ? $"执行失败。任务ID: {taskRef.taskId}"
                : $"{result.Error}\n任务ID: {taskRef.taskId}";
        }
    }

    private GameBridgeRequestContext BuildBridgeContext(AgentInvocationRequest request)
    {
        var current = _sessionExecutionContextAccessor?.Current;
        return new GameBridgeRequestContext
        {
            SessionId = current?.SessionId,
            GameId = request.State.Id,
            SourceId = current?.SourceId,
            ChannelId = current?.ChannelId,
            ActorId = current?.ActorId,
            Reason = "agent/command"
        };
    }
}
