using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using openLuo.Core.Interfaces;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;
using openLuo.Infrastructure.Database;
using openLuo.Modules.AppShell.Application;
using Microsoft.Data.Sqlite;

namespace openLuo.Modules.GameBridge.Infrastructure.Handlers;

public class LifecycleApiHandler(
    IGameStateRepository stateRepo,
    ICharacterRepository characterRepo,
    IGameLogger logger,
    IMemoryWriteService memoryWriteService,
    IDatabaseConnectionFactory connectionFactory,
    IStateQueryService stateQueryService,
    IStateMutationService stateMutationService,
    IStateRegistry stateRegistry,
    ITimeService timeService,
    IGameBridgeContextAccessor bridgeContextAccessor,
    IRuntimeConfigCenter? configCenter = null)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> CompressionGuards = new();
    private static readonly JsonSerializerOptions _serOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IRuntimeConfigCenter? _configCenter = configCenter;
    private IPluginHost? pluginHost;

    public void SetPluginHost(IPluginHost host) => pluginHost = host;

    private static string FormatGameTime(int minute) => $"{minute / 60:D2}:{minute % 60:D2}";

    private static string StageLabel(RelationshipStage stage) => stage switch
    {
        RelationshipStage.Stranger => "陌生人",
        RelationshipStage.Acquaintance => "熟人",
        RelationshipStage.Friend => "朋友",
        RelationshipStage.CloseFriend => "好友",
        RelationshipStage.Lover => "恋人",
        _ => "陌生人"
    };

    [GameApi("game/lifecycle/sleep")]
    public async Task<JsonNode?> GoToSleepAsync(string gameId)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null) return JsonNode.Parse("{\"ok\":false,\"error\":\"游戏未初始化\"}");
        var character = await characterRepo.GetByArchetypeIdAsync(state.ArchetypeId);
        if (character is null) return JsonNode.Parse("{\"ok\":false,\"error\":\"角色数据不存在\"}");
        var currentSnapshot = await timeService.GetSnapshotAsync(state.Id) ?? new TimeSnapshot
        {
            Day = state.CurrentDay,
            Minute = state.CurrentMinute,
            TimeStr = FormatGameTime(state.CurrentMinute),
            IsLate = state.CurrentMinute >= 1320,
            Mode = TimeMode.Virtual,
            EpochMs = ((long)Math.Max(1, state.CurrentDay) - 1L) * 86_400_000L + state.CurrentMinute * 60_000L
        };

        var staminaDef = stateRegistry.GetDef("game_resource", StateOwnerKind.Game, "stamina");
        var staminaStr = (await stateQueryService.GetAsync(state.Id, "game_resource", StateOwnerKind.Game, "global", "stamina")).Value;
        if (!double.TryParse(staminaStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var currentStamina))
            return JsonNode.Parse("{\"ok\":false,\"error\":\"资源系统未初始化\"}");

        var maxStamina = double.TryParse(staminaDef?.MaxValue, out var maxParsed) ? maxParsed : 100;
        if (currentStamina >= maxStamina)
            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                ok = true, refused = true, message = "你并不觉得累，现在还不想睡。"
            }, _serOpts));

        var oldDay = currentSnapshot.Day;
        var currentMinute = currentSnapshot.Minute;
        var wellRested = currentMinute < 1320;
        var lifecycle = _configCenter?.GetSnapshot().Lifecycle;
        var wakeUpBase = Math.Max(0, lifecycle?.WakeUpBaseMinute ?? 480);
        var lateThreshold = Math.Max(0, lifecycle?.LateSleepThresholdMinute ?? 120);
        var isEarlyMorning = currentMinute < wakeUpBase;
        var isLateSleep = isEarlyMorning && currentMinute >= lateThreshold;

        double restoreDelta;
        if (isLateSleep)
        {
            var penaltyFactor = Math.Clamp((currentMinute - lateThreshold) / (double)Math.Max(1, wakeUpBase - lateThreshold), 0.0, 1.0);
            var recoveryRate = 1.0 - penaltyFactor * 0.65;
            var normalDelta = maxStamina - currentStamina;
            restoreDelta = Math.Max(lifecycle?.MinimumRecoveryDelta ?? 20.0, normalDelta * recoveryRate);
        }
        else
            restoreDelta = maxStamina - currentStamina;

        var apply = await stateMutationService.ApplyAsync(state.Id,
        [
            new StateMutation
            {
                Namespace = "game_resource",
                Key = "stamina",
                OwnerKind = StateOwnerKind.Game,
                OwnerId = "global",
                Op = "delta",
                Value = ((int)Math.Floor(restoreDelta)).ToString(),
                SourceType = "lifecycle",
                SourceId = "sleep"
            }
        ]);
        var first = apply.FirstOrDefault();
        if (first is null || !first.Ok)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"体力更新失败\"}");
        var newStaminaStr = first.NewValue ?? staminaStr;
        int.TryParse(newStaminaStr, out var newStamina);

        var wakeUpMinute = isLateSleep ? wakeUpBase + Random.Shared.Next(0, 61) : wakeUpBase;
        var minutesToSleep = isEarlyMorning ? wakeUpMinute - currentMinute : (1440 - currentMinute) + wakeUpMinute;

        var advanced = await timeService.AdvanceAsync(state.Id, minutesToSleep, source: "lifecycle/sleep");
        var newDay = advanced.Snapshot.Day;
        state.CurrentDay = advanced.Snapshot.Day;
        state.CurrentMinute = advanced.Snapshot.Minute;

        var dayAdvanced = newDay > oldDay;

        if (pluginHost is not null)
        {
            var bridgeContext = BuildBridgeContext(state.Id, "lifecycle", "system", character.Id, "sleep");
            await pluginHost.CallHookAsync("onSleepAfter", new { oldDay, newDay, wellRested, characterId = character.Id }, context: bridgeContext);
            await pluginHost.CallHookAsync("onDayStart", new { characterId = character.Id, newDay, lastInteractionDay = state.LastInteractionDay }, context: bridgeContext);
        }

        _ = CompressionGuards;
        await memoryWriteService.WriteAsync(new openLuo.Modules.Memory.Core.Models.MemoryWriteInput
        {
            GameId = state.Id,
            CharacterId = character.Id,
            Scope = openLuo.Modules.Memory.Core.Models.MemoryScope.CharacterPrivate,
            RawContent = $"{character.Name} 陪伴玩家入睡。旧日期 {oldDay}，醒来日期 {newDay}，睡眠质量：{(wellRested ? "正常" : "较差")}。",
            Source = "lifecycle/sleep",
            Emotion = wellRested
                ? openLuo.Modules.Memory.Core.Models.MemoryEmotion.Positive
                : openLuo.Modules.Memory.Core.Models.MemoryEmotion.Mixed,
            Importance = dayAdvanced ? 0.45f : 0.3f,
            Metadata = new Dictionary<string, string>
            {
                ["old_day"] = oldDay.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["new_day"] = newDay.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["well_rested"] = wellRested ? "true" : "false"
            }
        });

        string message;
        if (isLateSleep)
        {
            var wakeTimeStr = FormatGameTime(wakeUpMinute);
            var restoredInt = newStamina - (int)currentStamina;
            message = $"{character.Name} 在 {wakeTimeStr} 拍拍你把你叫醒。\n昨晚睡得太晚，睡眠质量很差……体力只恢复了 {restoredInt} 点（{newStamina}/{(int)maxStamina}）。";
        }
        else if (!dayAdvanced)
            message = "你补了个觉，精神多了。体力完全恢复！";
        else if (wellRested)
            message = $"你早早入睡，睡得很香。体力完全恢复！\n第 {newDay} 天开始了。";
        else
            message = $"你倒头就睡，体力完全恢复。\n第 {newDay} 天开始了。";

        logger?.Info("lifecycle/sleep", $"sleep ok oldDay={oldDay} newDay={newDay} wellRested={wellRested}");
        return JsonNode.Parse(JsonSerializer.Serialize(new { ok = true, refused = false, message, newDay, wellRested }, _serOpts));
    }

    private async Task<GameState?> ResolveStateAsync(string? gameId = null)
    {
        if (!string.IsNullOrWhiteSpace(gameId))
            return await stateRepo.GetAsync(gameId);

        var contextGameId = bridgeContextAccessor.Current?.GameId;
        if (!string.IsNullOrWhiteSpace(contextGameId))
            return await stateRepo.GetAsync(contextGameId);

        return null;
    }

    private GameBridgeRequestContext BuildBridgeContext(string gameId, string sourceId, string channelId, string actorId, string reason) =>
        new()
        {
            SessionId = bridgeContextAccessor.Current?.SessionId,
            GameId = gameId,
            SourceId = sourceId,
            ChannelId = channelId,
            ActorId = actorId,
            Reason = reason
        };

    [GameApi("game/diary/write")]
    public async Task<JsonNode?> WriteGameDiaryAsync(string gameId, int day, string content)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        await using var conn = await connectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO diaries (id, game_id, day, content, created_at) VALUES (@Id, @GameId, @Day, @Content, @CreatedAt)",
            new
            {
                Id = Guid.NewGuid().ToString(),
                GameId = state.Id,
                Day = day,
                Content = content,
                CreatedAt = DateTime.UtcNow.ToString("O")
            });
        logger?.Info("diary/write", $"diary written day={day}");
        return JsonNode.Parse("{\"ok\":true}");
    }

    [GameApi("game/diary/list")]
    public async Task<JsonNode?> ListGameDiaryAsync(string gameId, int offset = 0, int limit = 10)
    {
        var state = await ResolveStateAsync(gameId);
        if (state is null)
            return JsonNode.Parse("{\"ok\":false,\"error\":\"no_active_game\"}");

        try
        {
            await using var conn = await connectionFactory.OpenAsync();
            var rows = await conn.QueryAsync<dynamic>(
                "SELECT id, day, content, created_at FROM diaries WHERE game_id = @gameId ORDER BY day DESC LIMIT @limit OFFSET @offset",
                new { gameId = state.Id, limit, offset });
            var diaries = rows.Select(r => new { id = (string)r.id, day = (int)r.day, content = (string)r.content, createdAt = (string)r.created_at });
            return JsonNode.Parse(JsonSerializer.Serialize(diaries, _serOpts));
        }
        catch { return JsonNode.Parse("[]"); }
    }
}
