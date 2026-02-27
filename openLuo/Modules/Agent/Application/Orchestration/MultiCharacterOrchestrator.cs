using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces;
using System.Text.Json;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class MultiCharacterOrchestrator : IMultiCharacterOrchestrator
{
    private readonly IAgentRuntimeHub _runtimeHub;
    private readonly IAgentRoster _roster;
    private readonly IAgentTaskStore _taskStore;
    private readonly IAgentProfileCatalog _profileCatalog;
    private readonly IMultiCharacterCommandCatalog _commandCatalog;
    private readonly IGameLogger? _logger;
    private readonly IRuntimeConfigCenter? _configCenter;

    public MultiCharacterOrchestrator(
        IAgentRuntimeHub runtimeHub,
        IAgentRoster roster,
        IAgentTaskStore taskStore,
        IAgentProfileCatalog profileCatalog,
        IMultiCharacterCommandCatalog commandCatalog,
        IGameLogger? logger = null,
        IRuntimeConfigCenter? configCenter = null)
    {
        _runtimeHub = runtimeHub;
        _roster = roster;
        _taskStore = taskStore;
        _profileCatalog = profileCatalog;
        _commandCatalog = commandCatalog;
        _logger = logger;
        _configCenter = configCenter;
    }

    private TimeSpan TaskDispatchTimeout => TimeSpan.FromSeconds(Math.Max(1, _configCenter?.GetSnapshot().Agent.TaskDispatchTimeoutSeconds ?? 24));

    public bool CanHandle(ParsedCommand command)
    {
        if (command.Kind != InvocationKind.Command)
            return false;
        return _commandCatalog.GetCommands().Any(c =>
            c.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Contains(command.Name, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<CommandDescriptor> GetRegisteredCommands() => _commandCatalog.GetCommands();

    public Task<CommandResult> ExecuteAsync(MultiCharacterCommandContext context, CancellationToken ct = default)
    {
        if (context.CommandName.Equals("task", StringComparison.OrdinalIgnoreCase) ||
            context.CommandName.Equals("party", StringComparison.OrdinalIgnoreCase))
            return ExecuteTaskAsync(context, ct);
        if (context.CommandName.Equals("characters", StringComparison.OrdinalIgnoreCase))
            return ExecuteCharactersAsync(context, ct);
        if (context.CommandName.Equals("switch", StringComparison.OrdinalIgnoreCase))
            return ExecuteSwitchAsync(context, ct);
        if (context.CommandName.Equals("task_status", StringComparison.OrdinalIgnoreCase) ||
            context.CommandName.Equals("taskstatus", StringComparison.OrdinalIgnoreCase))
            return ExecuteTaskStatusAsync(context, ct);

        return Task.FromResult(CommandResult.Fail($"暂不支持命令：/{context.CommandName}"));
    }

    private async Task<CommandResult> ExecuteTaskAsync(MultiCharacterCommandContext context, CancellationToken ct)
    {
        if (context.Args.Length == 0)
            return CommandResult.Fail("请输入任务内容，例：/task 帮我规划今晚学习安排");

        await _runtimeHub.EnsurePartyStartedAsync(context.State.Id, ct);
        var taskText = string.Join(' ', context.Args).Trim();
        var participants = await ResolveParticipantsAsync(context, ct);
        var correlationId = $"task_{Guid.NewGuid():N}";
        var taskContext = JsonSerializer.Serialize(new
        {
            source = "multi_character",
            command = context.CommandName,
            participants
        });
        var taskId = await _taskStore.CreateTaskAsync(
            context.State.Id,
            taskText,
            "player",
            taskContext,
            ct);
        var now = DateTime.UtcNow;
        var steps = participants.Select((character, index) => new PartyTaskStepRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            StepOrder = index + 1,
            AssignedCharacterId = character,
            Role = index == 0 ? "lead" : "support",
            Instruction = taskText,
            ResultJson = "{}",
            Status = "running",
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
        await _taskStore.CreateStepsAsync(taskId, steps, ct);

        var replies = await _runtimeHub.RequestManyAsync(
            characterIds: participants,
            type: AgentMessageType.TaskAssign,
            from: "dispatcher",
            payload: taskText,
            gameId: context.State.Id,
            correlationId: correlationId,
            timeout: TaskDispatchTimeout,
            ct: ct);

        if (replies.Count == 0)
        {
            await _taskStore.UpdateTaskStatusAsync(taskId, "failed", ct);
            return CommandResult.Fail("协作任务未收到任何角色回应，请稍后重试。");
        }

        foreach (var step in steps)
        {
            var reply = replies.FirstOrDefault(r => string.Equals(
                r.From.Trim(),
                step.AssignedCharacterId.Trim(),
                StringComparison.OrdinalIgnoreCase));
            if (reply is null)
            {
                await _taskStore.UpdateStepResultAsync(step.Id, "failed", JsonSerializer.Serialize(new
                {
                    error = "no_reply"
                }), ct);
                continue;
            }

            await _taskStore.UpdateStepResultAsync(step.Id, "done", JsonSerializer.Serialize(new
            {
                reply.From,
                reply.Payload,
                reply.TimestampUtc,
                traceLines = reply.TraceLines
            }), ct);
        }
        await _taskStore.UpdateTaskStatusAsync(taskId, "done", ct);

        var lines = new List<string>
        {
            $"任务已分发（{participants.Count} 位角色）：{taskText}",
            $"协作ID: {correlationId}",
            $"任务ID: {taskId}"
        };

        foreach (var reply in replies)
        {
            lines.Add($"- {ResolveDisplayName(reply.From)}: {reply.Payload}");
            if (reply.TraceLines is { Count: > 0 })
            {
                foreach (var trace in reply.TraceLines)
                    lines.Add($"  · {trace}");
            }
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }

    private async Task<CommandResult> ExecuteTaskStatusAsync(MultiCharacterCommandContext context, CancellationToken ct)
    {
        if (context.Args.Length == 0)
        {
            var recent = await _taskStore.ListRecentTasksAsync(context.State.Id, 5, ct);
            if (recent.Count == 0)
                return CommandResult.Ok("暂无协作任务记录。");

            var lines = new List<string> { "最近任务：" };
            foreach (var taskItem in recent)
            {
                var kindTag = ReadTaskContextValue(taskItem.ContextJson, "invocationKind")
                    ?? ReadTaskContextValue(taskItem.ContextJson, "source")
                    ?? "command";
                lines.Add($"- {taskItem.Id} [{taskItem.Status}] ({kindTag}) {taskItem.Title}");
            }
            lines.Add("使用 /task_status <任务ID> 查看详情。");
            return CommandResult.Ok(string.Join("\n", lines));
        }

        var taskId = context.Args[0].Trim();
        var taskRecord = await _taskStore.GetTaskAsync(context.State.Id, taskId, ct);
        if (taskRecord is null)
            return CommandResult.Fail($"未找到任务：{taskId}");

        var steps = await _taskStore.ListStepsAsync(taskId, ct);
        var lines2 = new List<string>
        {
            $"任务：{taskRecord.Title}",
            $"ID: {taskRecord.Id}",
            $"状态：{taskRecord.Status}",
            $"创建时间：{taskRecord.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC"
        };
        var invocationKind = ReadTaskContextValue(taskRecord.ContextJson, "invocationKind")
            ?? ReadTaskContextValue(taskRecord.ContextJson, "source");
        if (!string.IsNullOrWhiteSpace(invocationKind))
            lines2.Add($"类型：{invocationKind}");
        var riskLevel = ReadTaskContextValue(taskRecord.ContextJson, "riskLevel");
        if (!string.IsNullOrWhiteSpace(riskLevel))
            lines2.Add($"风险：{riskLevel}");
        if (steps.Count > 0)
        {
            lines2.Add("步骤：");
            foreach (var group in steps.GroupBy(x => x.Role).OrderBy(g => RoleOrder(g.Key)))
            {
                lines2.Add($"  [{FormatRole(group.Key)}]");
                foreach (var step in group.OrderBy(x => x.StepOrder))
                {
                    lines2.Add($"  - #{step.StepOrder} {ResolveDisplayName(step.AssignedCharacterId)} [{step.Status}]");
                    var pretty = FormatStepResult(step.ResultJson);
                    if (!string.IsNullOrWhiteSpace(pretty))
                        lines2.Add($"    结果: {pretty}");
                }
            }
        }
        return CommandResult.Ok(string.Join("\n", lines2));
    }

    private async Task<string> ResolveTargetCharacterIdAsync(MultiCharacterCommandContext context, CancellationToken ct)
    {
        if (context.Options.TryGetValue("as", out var selected) && !string.IsNullOrWhiteSpace(selected))
        {
            var target = await _roster.ResolveAsync(context.State.Id, selected.Trim(), ct);
            return target?.Id ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(context.ActiveCharacter.Id))
        {
            var byId = await _roster.ResolveAsync(context.State.Id, context.ActiveCharacter.Id.Trim(), ct);
            if (byId is not null)
                return byId.Id;
        }

        if (!string.IsNullOrWhiteSpace(context.ActiveCharacter.Name))
        {
            var byName = await _roster.ResolveAsync(context.State.Id, context.ActiveCharacter.Name.Trim(), ct);
            if (byName is not null)
                return byName.Id;
        }

        if (!string.IsNullOrWhiteSpace(context.ActiveCharacter.ArchetypeId))
        {
            var byArchetype = await _roster.ResolveAsync(context.State.Id, context.ActiveCharacter.ArchetypeId.Trim(), ct);
            if (byArchetype is not null)
                return byArchetype.Id;
        }

        return context.ActiveCharacter.Id.Trim().ToLowerInvariant();
    }

    private async Task<List<string>> ResolveParticipantsAsync(MultiCharacterCommandContext context, CancellationToken ct)
    {
        if (context.Options.TryGetValue("team", out var teamValue) && !string.IsNullOrWhiteSpace(teamValue))
        {
            var selected = new List<string>();
            foreach (var item in teamValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var target = await _roster.ResolveAsync(context.State.Id, item, ct);
                if (target is not null)
                    selected.Add(target.Id);
            }
            selected = selected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count > 0)
                return selected;
        }

        var stored = (await _roster.ListAsync(context.State.Id, ct))
            .Select(x => x.Id.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (stored.Count > 0)
            return stored;

        return [context.ActiveCharacter.Id.Trim().ToLowerInvariant()];
    }

    private async Task<CommandResult> ExecuteCharactersAsync(MultiCharacterCommandContext context, CancellationToken ct)
    {
        var list = await _roster.ListAsync(context.State.Id, ct);
        if (list.Count == 0)
            return CommandResult.Fail("当前存档还没有可用角色。");

        var lines = new List<string> { "可用角色：" };
        foreach (var character in list)
        {
            var active = string.Equals(character.Id, context.State.ActiveCharacterId, StringComparison.OrdinalIgnoreCase)
                ? " [当前]"
                : string.Empty;
            lines.Add($"- {character.Name} ({character.Id}){active}");
        }
        lines.Add("使用 /switch <角色名|角色ID> 切换当前角色，或 /chat --as <角色> 指定本次对话角色。");
        return CommandResult.Ok(string.Join("\n", lines));
    }

    private async Task<CommandResult> ExecuteSwitchAsync(MultiCharacterCommandContext context, CancellationToken ct)
    {
        if (context.Args.Length == 0)
            return CommandResult.Fail("请输入目标角色，例：/switch 铃");

        var selector = context.Args[0].Trim().ToLowerInvariant();
        var target = await _roster.SetActiveAsync(context.State.Id, selector, ct);
        if (target is null)
            return CommandResult.Fail($"找不到角色：{context.Args[0]}");
        return CommandResult.Ok($"已切换当前角色为：{target.Name} ({target.Id})");
    }

    private string ResolveDisplayName(string characterId)
    {
        return _profileCatalog.GetProfile(characterId).DisplayName;
    }

    private static string? FormatStepResult(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson) || resultJson == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return resultJson;

            var hasPayload = root.TryGetProperty("Payload", out var payload) || root.TryGetProperty("payload", out payload);
            var hasFrom = root.TryGetProperty("From", out var from) || root.TryGetProperty("from", out from);
            if (hasPayload && hasFrom)
                return Clip($"{from.GetString()}: {payload.GetString()}");

            if (root.TryGetProperty("error", out var error))
                return Clip($"error={error.GetString()}");

            var fields = new List<string>();
            foreach (var p in root.EnumerateObject())
            {
                if (fields.Count >= 3)
                    break;
                fields.Add($"{p.Name}={p.Value.ToString()}");
            }
            return fields.Count == 0 ? null : Clip(string.Join(", ", fields));
        }
        catch
        {
            return Clip(resultJson);
        }
    }

    private static string Clip(string? text, int maxLen = 180)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
            return text ?? string.Empty;
        return text[..maxLen] + "...";
    }

    private static string? ReadTaskContextValue(string? contextJson, string key)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty(key, out var value))
                return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                _ => value.ToString()
            };
        }
        catch
        {
            return null;
        }
    }

    private static int RoleOrder(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "lead" => 0,
            "support" => 1,
            "reviewer" => 2,
            "skill" => 3,
            "tool" => 4,
            "subagent" => 5,
            _ => 9
        };
    }

    private static string FormatRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "lead" => "主执行",
            "support" => "协作",
            "reviewer" => "审核",
            "skill" => "Skill",
            "tool" => "Tool",
            "subagent" => "SubAgent",
            _ => role
        };
    }
}
