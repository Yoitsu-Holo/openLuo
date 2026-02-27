using System.Linq;
using System.Text.RegularExpressions;
using openLuo.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
namespace openLuo.Modules.Gameplay.Application.Services;

public class GameEngine(
    IGameStateRepository stateRepo,
    ICharacterRepository characterRepo,
    ISessionBootstrapper sessionBootstrapper,
    IAgentInvocationRouter invocationRouter,
    IGameLogger? logger = null,
    ICommandGate? commandGate = null) : IGameEngine
{
    public async Task<CommandResult> ExecuteAsync(string gameId, string rawInput, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var parsed = ParseCommand(rawInput.Trim());
        if (parsed is null)
            return CommandResult.Fail("请输入有效的指令。输入 /help 查看所有指令。");

        if (invocationRouter.CanHandle(parsed))
        {
            var state = await stateRepo.GetAsync(gameId, ct) ?? throw new InvalidOperationException("游戏未初始化。");
            var character = await ResolveCharacterForCommandAsync(state, parsed, ct)
                ?? throw new InvalidOperationException("角色数据不存在。");

            var gateContext = new CommandGateContext
            {
                GameId = state.Id,
                RawInput = rawInput,
                CommandName = parsed.Name,
                Args = parsed.Args,
                Options = parsed.Options
            };
            CommandGateBeforeResult? gateResult = null;
            if (commandGate is not null)
            {
                gateResult = await commandGate.BeforeExecuteAsync(gateContext, ct);
                if (!gateResult.Allow)
                {
                    var blockedMessage = SanitizeForTerminal(gateResult.Message ?? $"当前时段不可执行 /{parsed.Name}");
                    var noticeText = BuildNoticeText(gateResult);
                    var errorText = string.IsNullOrWhiteSpace(noticeText)
                        ? blockedMessage
                        : $"{noticeText}\n{blockedMessage}";
                    return CommandResult.Fail(errorText);
                }
            }

            logger?.Debug("engine", $"exec /{parsed.Name}", new { args = parsed.Args });
            var result = await invocationRouter.ExecuteAsync(new AgentInvocationRequest
            {
                RawInput = rawInput,
                Parsed = parsed,
                State = state,
                ActiveCharacter = character
            }, ct);
            logger?.Info("engine", $"/{parsed.Name} [{(result.Success ? "ok" : "fail")}]",
                result.Success ? null : new { error = result.Error });

            if (commandGate is not null)
                await commandGate.AfterExecuteAsync(gateContext, result, ct);

            if (!string.IsNullOrWhiteSpace(result.Output))
                result.Output = SanitizeForTerminal(result.Output);
            if (!string.IsNullOrWhiteSpace(result.Error))
                result.Error = SanitizeForTerminal(result.Error);

            var resultNoticeText = BuildNoticeText(gateResult);
            if (!string.IsNullOrWhiteSpace(resultNoticeText))
            {
                if (result.Success)
                {
                    result.Output = string.IsNullOrWhiteSpace(result.Output)
                        ? resultNoticeText
                        : $"{resultNoticeText}\n{result.Output}";
                }
                else
                {
                    result.Error = string.IsNullOrWhiteSpace(result.Error)
                        ? resultNoticeText
                        : $"{resultNoticeText}\n{result.Error}";
                }
            }
            return result;
        }

        logger?.Warn("engine", $"unknown command: /{parsed.Name}");
        return CommandResult.Fail($"未知指令：{parsed.Prefix}{parsed.Name}。输入 /help 查看所有指令。");
    }

    public async Task<GameState> GetStateAsync(string gameId, CancellationToken ct = default) =>
        await stateRepo.GetAsync(gameId, ct) ?? throw new InvalidOperationException("游戏未初始化。");

    public async Task<string> InitializeAsync(string gameId, string archetypeId, string playerName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var existing = await stateRepo.GetAsync(gameId, ct);
        if (existing is not null)
            return existing.Id;

        var result = await sessionBootstrapper.BootstrapAsync(new SessionBootstrapRequest
        {
            SessionId = gameId,
            PlayerName = playerName,
            SelectedCharacterIds = [archetypeId],
            ActiveCharacterId = $"char_{Normalize(archetypeId)}"
        }, ct);

        if (result.Diagnostics.Count > 0)
        {
            logger?.Warn("engine", "bootstrap diagnostics during initialize", new
            {
                archetypeId,
                diagnostics = result.Diagnostics.Select(x => $"{x.Code}:{x.Message}").ToArray()
            });
        }

        return gameId;
    }

    private async Task<Character?> ResolveCharacterForCommandAsync(GameState state, ParsedCommand parsed, CancellationToken ct)
    {
        var requested = parsed.Options.TryGetValue("as", out var asValue)
            ? Normalize(asValue)
            : string.Empty;
        var activeCharacterId = Normalize(state.ActiveCharacterId);

        if (!string.IsNullOrEmpty(requested))
        {
            var candidate = await ResolveCharacterBySelectorAsync(state, requested, ct);
            if (candidate is not null)
            {
                if (!string.Equals(state.ActiveCharacterId, candidate.Id, StringComparison.OrdinalIgnoreCase))
                {
                    state.ActiveCharacterId = candidate.Id;
                    await stateRepo.SaveAsync(state, ct);
                }
                return candidate;
            }
        }

        if (!string.IsNullOrEmpty(activeCharacterId))
        {
            var byActive = await characterRepo.GetByIdAsync(state.Id, activeCharacterId, ct);
            if (byActive is not null)
                return byActive;
        }

        var byArchetype = await characterRepo.GetByArchetypeIdAsync(state.ArchetypeId, ct);
        if (byArchetype is not null)
            return byArchetype;

        var list = await characterRepo.ListByGameIdAsync(state.Id, ct);
        return list.FirstOrDefault();
    }

    private async Task<Character?> ResolveCharacterBySelectorAsync(GameState state, string selector, CancellationToken ct)
    {
        var byId = await characterRepo.GetByIdAsync(state.Id, selector, ct);
        if (byId is not null)
            return byId;

        var all = await characterRepo.ListByGameIdAsync(state.Id, ct);
        return all.FirstOrDefault(c =>
            string.Equals(Normalize(c.Name), selector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Normalize(c.ArchetypeId), selector, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static ParsedCommand? ParseCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var prefix = input[0];
        if (prefix is not '/' and not '$' and not '&' and not '@') return null;
        var parts = input[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var options = new Dictionary<string, string>();
        var args = new List<string>();
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("--") && i + 1 < parts.Length)
                options[parts[i][2..]] = parts[++i];
            else
                args.Add(parts[i]);
        }
        return new ParsedCommand
        {
            Kind = prefix switch
            {
                '$' => InvocationKind.Skill,
                '&' => InvocationKind.SubAgent,
                '@' => InvocationKind.Tool,
                _ => InvocationKind.Command
            },
            Prefix = prefix,
            Name = parts[0],
            Args = [.. args],
            Options = options
        };
    }

    private static string SanitizeForTerminal(string text)
    {
        var noAnsi = Regex.Replace(text ?? string.Empty, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
        return new string(noAnsi.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray()).Trim();
    }

    private static string BuildNoticeText(CommandGateBeforeResult? gateResult)
    {
        if (gateResult is null || gateResult.Notices.Count == 0)
            return string.Empty;

        var notices = gateResult.Notices
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(SanitizeForTerminal)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
        return notices.Count == 0 ? string.Empty : string.Join("\n", notices);
    }
}
