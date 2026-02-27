using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Linq;
using openLuo.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.Gameplay.Application.Services;

public class CommandGate(
    IGameStateRepository stateRepo,
    ITimeService timeService,
    ITimelineService timelineService,
    IPluginHost pluginHost,
    IGameLogger logger) : ICommandGate
{
    private static readonly ConcurrentDictionary<string, int> LunchNoticeDay = new();
    private static readonly HashSet<string> ClassWindowAllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "status", "chat", "plan_date", "time_mode", "debug"
    };

    public async Task<CommandGateBeforeResult> BeforeExecuteAsync(CommandGateContext context, CancellationToken ct = default)
    {
        var result = new CommandGateBeforeResult { Allow = true };

        var state = await ResolveStateAsync(context.GameId, ct);
        if (state is null) return result;

        var snapshot = await timeService.TickAsync(state.Id, ct) ?? await timeService.GetSnapshotAsync(state.Id, ct);
        if (snapshot?.Mode == TimeMode.Disabled)
        {
            logger?.Debug("command/gate", $"skip gate in disabled mode cmd=/{context.CommandName}");
            return result;
        }

        var nowEpochMs = snapshot?.EpochMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var dueEvents = await timelineService.PollDueAsync(state.Id, nowEpochMs, 32, ct) ?? [];
        result.DueEvents = dueEvents;
        string? timelineBlockMessage = null;

        foreach (var evt in dueEvents)
        {
            PluginHookResult? hookResult = null;
            try
            {
                hookResult = await pluginHost.CallHookAsync("onScheduleDue", new
                {
                    eventId = evt.Id,
                    eventType = evt.EventType,
                    title = evt.Title,
                    dueAtEpochMs = evt.DueAtEpochMs,
                    status = evt.Status,
                    actionJson = evt.ActionJson,
                    contextJson = evt.ContextJson,
                    commandName = context.CommandName,
                    commandArgs = context.Args,
                    nowEpochMs,
                    mode = snapshot?.Mode.ToString().ToLowerInvariant() ?? "virtual"
                }, ct, new GameBridgeRequestContext
                {
                    GameId = state.Id,
                    SourceId = "command_gate",
                    ChannelId = "system",
                    ActorId = "system",
                    Reason = "onScheduleDue"
                });
            }
            catch (Exception ex)
            {
                logger?.Warn("command/gate", $"onScheduleDue failed evt={evt.Id}: {ex.Message}");
            }

            var actionMessage = TryExtractActionMessage(evt);
            if (!string.IsNullOrWhiteSpace(actionMessage))
                result.Notices.Add(SanitizeForTerminal(actionMessage));
            if (!string.IsNullOrWhiteSpace(hookResult?.AdditionalText))
                result.Notices.Add(SanitizeForTerminal(hookResult.AdditionalText!.Trim()));

            if (TryBuildBlockMessage(evt, context.CommandName, out var msg))
            {
                timelineBlockMessage ??= msg;
            }

            try
            {
                var acked = await timelineService.AckAsync(state.Id, evt.Id, TimelineEventStatus.Done, ct);
                if (!acked)
                {
                    logger?.Warn("command/gate", $"auto-ack returned false evt={evt.Id}, fallback cancel");
                    var cancelled = await timelineService.CancelAsync(state.Id, evt.Id, ct);
                    if (!cancelled)
                        logger?.Warn("command/gate", $"fallback cancel failed evt={evt.Id}");
                }
            }
            catch (Exception ex)
            {
                logger?.Warn("command/gate", $"auto-ack failed evt={evt.Id}: {ex.Message}");
                try
                {
                    var cancelled = await timelineService.CancelAsync(state.Id, evt.Id, ct);
                    if (!cancelled)
                        logger?.Warn("command/gate", $"fallback cancel failed evt={evt.Id}");
                }
                catch (Exception cex)
                {
                    logger?.Warn("command/gate", $"fallback cancel exception evt={evt.Id}: {cex.Message}");
                }
            }
        }

        ApplyFixedWindowPolicies(state.Id, snapshot, context, result);
        if (!result.Allow)
            return result;

        if (!string.IsNullOrWhiteSpace(timelineBlockMessage))
        {
            result.Allow = false;
            result.Message = SanitizeForTerminal(timelineBlockMessage);
            logger?.Info("command/gate", $"blocked command /{context.CommandName} by timeline");
            return result;
        }

        return result;
    }

    public async Task AfterExecuteAsync(CommandGateContext context, CommandResult result, CancellationToken ct = default)
    {
        try
        {
            var snapshot = string.IsNullOrWhiteSpace(context.GameId)
                ? null
                : await timeService.GetSnapshotAsync(context.GameId, ct);
            await pluginHost.CallHookAsync("onCommandAfter", new
            {
                commandName = context.CommandName,
                commandArgs = context.Args,
                success = result.Success,
                error = result.Error,
                mode = snapshot?.Mode.ToString().ToLowerInvariant() ?? "virtual",
                day = snapshot?.Day ?? 0,
                minute = snapshot?.Minute ?? 0
            }, ct, new GameBridgeRequestContext
            {
                GameId = context.GameId,
                SourceId = "command_gate",
                ChannelId = "system",
                ActorId = "system",
                Reason = "onCommandAfter"
            });
        }
        catch (Exception ex)
        {
            logger?.Warn("command/gate", $"onCommandAfter failed cmd=/{context.CommandName}: {ex.Message}");
        }
    }

    private async Task<GameState?> ResolveStateAsync(string? gameId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
            return await stateRepo.GetAsync(gameId, ct);

        return null;
    }

    private static bool TryBuildBlockMessage(TimelineEvent evt, string commandName, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(evt.ActionJson))
            return false;
        if (System.Text.Encoding.UTF8.GetByteCount(evt.ActionJson) > TimelineLimits.MaxActionJsonBytes)
            return false;

        try
        {
            var node = JsonNode.Parse(evt.ActionJson);
            if (node is not JsonObject obj)
                return false;

            if (obj["lockCommands"] is not JsonArray arr || arr.Count == 0)
                return false;

            var matched = arr
                .Select(x => x?.GetValue<string>()?.Trim() ?? string.Empty)
                .Any(cmd => cmd.Equals("*", StringComparison.OrdinalIgnoreCase)
                            || cmd.Equals(commandName, StringComparison.OrdinalIgnoreCase));

            if (!matched)
                return false;

            message = obj["message"]?.GetValue<string>()
                      ?? $"当前时段不允许执行 /{commandName}。";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyFixedWindowPolicies(
        string gameId,
        TimeSnapshot? snapshot,
        CommandGateContext context,
        CommandGateBeforeResult result)
    {
        if (snapshot is null) return;

        // 09:30 - 11:30: class window, only whitelisted commands are allowed.
        if (IsInWindow(snapshot.Minute, 570, 690)
            && !ClassWindowAllowList.Contains(context.CommandName))
        {
            result.Allow = false;
            result.Message = "现在是上课时段（09:30-11:30），该操作暂不可用。可尝试 /chat 或 /status。";
            logger?.Info("command/gate", $"blocked by class-window cmd=/{context.CommandName}");
            return;
        }

        // 12:00 - 13:00: lunch window prompt + work restriction sample.
        if (IsInWindow(snapshot.Minute, 720, 780))
        {
            var lastNoticeDay = LunchNoticeDay.GetOrAdd(gameId, -1);
            if (lastNoticeDay != snapshot.Day)
            {
                LunchNoticeDay[gameId] = snapshot.Day;
                result.Notices.Add(SanitizeForTerminal("🍱 到午餐时间了，可以先去食堂休息一下。"));
            }

            if (context.CommandName.Equals("work", StringComparison.OrdinalIgnoreCase))
            {
                result.Allow = false;
                result.Message = "现在是午餐时段（12:00-13:00），先去吃饭再继续工作吧。";
                logger?.Info("command/gate", $"blocked by lunch-window cmd=/{context.CommandName}");
            }
        }
    }

    private static bool IsInWindow(int minute, int startInclusive, int endInclusive) =>
        minute >= startInclusive && minute <= endInclusive;

    private static string? TryExtractActionMessage(TimelineEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.ActionJson))
            return null;
        if (System.Text.Encoding.UTF8.GetByteCount(evt.ActionJson) > TimelineLimits.MaxActionJsonBytes)
            return null;

        try
        {
            var node = JsonNode.Parse(evt.ActionJson);
            if (node is not JsonObject obj)
                return null;
            return obj["message"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeForTerminal(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var noAnsi = Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
        return new string(noAnsi.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray()).Trim();
    }
}
