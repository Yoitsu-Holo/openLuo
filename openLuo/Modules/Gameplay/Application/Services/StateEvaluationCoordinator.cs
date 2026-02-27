using openLuo.Core.Interfaces;
using openLuo.Modules.Gameplay.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace openLuo.Modules.Gameplay.Application.Services;

/// <summary>
/// Coordinates state evaluation across plugins and LLM.
/// Implements the state-evaluation pipeline:
/// collect plugin prompt fragments → one LLM call → validate/clamp → apply.
/// </summary>
public class StateEvaluationCoordinator : IStateEvaluationCoordinator
{
    private readonly ILlmClient _llmClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StateEvaluationCoordinator> _logger;
    private readonly IStateMutationService _stateMutationService;
    private readonly ContentRegistry _contentRegistry;
    private readonly IResourceEvaluationProjectionService _resourceEvaluationProjectionService;

    public StateEvaluationCoordinator(
        ILlmClient llmClient,
        IServiceProvider serviceProvider,
        ILogger<StateEvaluationCoordinator> logger,
        IStateMutationService stateMutationService,
        ContentRegistry contentRegistry,
        IResourceEvaluationProjectionService resourceEvaluationProjectionService)
    {
        _llmClient = llmClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _stateMutationService = stateMutationService;
        _contentRegistry = contentRegistry;
        _resourceEvaluationProjectionService = resourceEvaluationProjectionService;
    }

    public async Task<StateEvaluationResult> EvaluateStatesAsync(
        string gameId,
        string characterId,
        string archetypeId,
        string beatSummary,
        string[] moodSignals,
        string playerMessage,
        string interactionType,
        CancellationToken cancellationToken = default)
    {
        var resourceSnapshot = await _resourceEvaluationProjectionService.BuildEvaluationSnapshotAsync(new ResourceEvaluationQuery
        {
            GameId = gameId,
            CharacterId = characterId,
            ArchetypeId = archetypeId
        }, cancellationToken);

        var allDefs = resourceSnapshot.Items.Select(ToEvalDef).ToList();
        var snapshot = resourceSnapshot.Items
            .GroupBy(item => item.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        var availableResourceIds = allDefs
            .Where(def => def.MutableByLlm && !def.Derived && def.LifecycleState is ResourceLifecycleState.Active or ResourceLifecycleState.Hidden)
            .Select(d => d.ResourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 2. 调用插件 onPromptContext(phase=chat-evaluate) 收集语义 fragments

        var hookInput = new OnPromptContextInput
        {
            Phase = "chat-evaluate",
            CharacterId = characterId,
            ArchetypeId = archetypeId,
            InteractionType = interactionType,
            PlayerMessage = playerMessage,
            BeatSummary = beatSummary,
            MoodSignals = [.. moodSignals],
            AvailableResources = availableResourceIds,
            StateSnapshot = resourceSnapshot.StateSnapshot,
            PluginConfigs = _contentRegistry.GetMergedPluginConfigs(characterId, archetypeId)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
        };
        var pluginHost = _serviceProvider.GetService<IPluginHost>();
        var fragments = pluginHost is null
            ? []
            : await pluginHost.CallPromptContextHookAsync(hookInput, cancellationToken);

        // 3. 构造统一评估 Prompt
        var baseContext = BuildStatePromptContext(allDefs, snapshot);
        var prompt = BuildEvaluationPrompt(baseContext, fragments.Select(f => f.Text),
            beatSummary, moodSignals, playerMessage, interactionType, availableResourceIds);

        // 4. 一次性 LLM 评估
        var messages = new[] { new ChatMessage(ChatMessageRole.User, prompt) };
        var raw = await _llmClient.CompleteAsync(
            messages,
            new LlmOptions
            {
                Temperature = 0.3f,
                MaxTokens = 2048,
                JsonMode = true
            },
            ct: cancellationToken);
        var llmResponse = ParseEvaluationResponse(raw) ?? new LlmEvaluationResponse([], string.Empty);

        // 5. 校验并结算（clamp、禁写检查、maxDeltaPerTurn 限制）
        var appliedChanges = await ValidateAndApplyChangesAsync(gameId, llmResponse.StateChanges, snapshot, allDefs);
        var validChanges = appliedChanges
            .Select(change => new StateChange(change.ResourceId, "set", change.NewValue))
            .ToArray();

        return new StateEvaluationResult(validChanges, llmResponse.Reason)
        {
            AppliedChanges = appliedChanges
        };
    }

    private string BuildEvaluationPrompt(
        string resourceContext,
        IEnumerable<string> pluginFragments,
        string beatSummary,
        string[] moodSignals,
        string playerMessage,
        string interactionType,
        List<string> availableResources)
    {
        var fragmentText = string.Join("\n\n", pluginFragments.Where(f => !string.IsNullOrWhiteSpace(f)));
        var resourceList = string.Join(", ", availableResources);

        return $@"你是一个游戏资源结算评估器。根据本轮互动，评估需要变化的资源。

## 当前资源状态
{resourceContext}

## 资源插件语义约束
{(string.IsNullOrWhiteSpace(fragmentText) ? "（无插件提供语义约束）" : fragmentText)}

## 本轮互动
- 互动类型：{interactionType}
- 玩家输入：{playerMessage}
- 节拍摘要：{beatSummary}
- 情绪信号：{string.Join(", ", moodSignals)}

## 输出要求
只输出一个合法的 JSON 对象，不要输出 markdown 代码块围栏，不要输出额外解释：
{{
  ""resourceChanges"": [
    {{""resourceId"": ""<资源ID>"", ""op"": ""delta""|""set"", ""value"": ""<数值>""}},
    ...
  ],
  ""reason"": ""<简要说明本轮资源变化原因>""
}}

约束：
- 只能修改以下已注册资源：{resourceList}
- 派生资源不可直接修改
- 请对所有可评估资源都给出一个条目；数值型未变化时返回 delta=0，枚举/文本型未变化时返回 set 为当前值
- 变化应谨慎、可解释，优先考虑 trust、mood、stress、dependency、shame、possessiveness、lust、sexual_intent 等与对话直接相关的资源
- affection/trust/stress/shame/dependency/possessiveness/lust/sexual_intent 推荐使用 delta
- mood 等枚举型推荐使用 set";
    }

    private static string BuildStatePromptContext(IEnumerable<EvalDef> defs, IReadOnlyDictionary<string, string> snapshot)
    {
        var lines = defs
            .OrderBy(d => d.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(d =>
            {
                snapshot.TryGetValue(d.ResourceId, out var value);
                var head = $"- {d.ResourceId}: {(string.IsNullOrWhiteSpace(value) ? "<empty>" : value)}";
                return string.IsNullOrWhiteSpace(d.PromptContext) ? head : $"{head}（{d.PromptContext}）";
            })
            .ToList();

        return lines.Count == 0 ? "（无可评估状态）" : string.Join("\n", lines);
    }

    private async Task<StateAppliedChange[]> ValidateAndApplyChangesAsync(
        string gameId, StateChange[] changes,
        Dictionary<string, string> snapshotToUpdate,
        IReadOnlyList<EvalDef> defs)
    {
        var valid = new List<StateAppliedChange>();
        var defByResourceId = defs
            .GroupBy(d => d.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var rawChange in changes)
        {
            var change = rawChange;
            if (!defByResourceId.TryGetValue(change.ResourceId, out var def))
            {
                _logger.LogWarning("Resource {ResourceId} not registered, skipping", change.ResourceId);
                continue;
            }
            if (!def.MutableByLlm)
            {
                _logger.LogWarning("Resource {ResourceId} not mutable by LLM (mutableByLlm=false), skipping", change.ResourceId);
                continue;
            }
            if (def.Derived)
            {
                _logger.LogWarning("Resource {ResourceId} is derived, LLM cannot modify directly, skipping", change.ResourceId);
                continue;
            }
            if (def.LifecycleState is not (ResourceLifecycleState.Active or ResourceLifecycleState.Hidden))
            {
                _logger.LogWarning("Resource {ResourceId} lifecycle={LifecycleState}, skipping", change.ResourceId, def.LifecycleState);
                continue;
            }

            // maxDeltaPerTurn 限制
            if (def.MaxDeltaPerTurn.HasValue && change.Op == "delta"
                && double.TryParse(change.Value, out var deltaVal))
            {
                var limit = def.MaxDeltaPerTurn.Value;
                if (Math.Abs(deltaVal) > limit)
                {
                    var clamped = Math.Sign(deltaVal) * limit;
                    _logger.LogInformation("Resource {ResourceId} delta {Delta} clamped to maxDeltaPerTurn {Limit}",
                        change.ResourceId, deltaVal, clamped);
                    change = change with { Value = clamped.ToString("F0") };
                }
            }

            {
                if (change.Op == "delta" && double.TryParse(change.Value, out var deltaCheck) && Math.Abs(deltaCheck) < 0.0001)
                {
                    snapshotToUpdate[change.ResourceId] = snapshotToUpdate.GetValueOrDefault(change.ResourceId, "0");
                    continue;
                }

                if (change.Op == "set" &&
                    snapshotToUpdate.TryGetValue(change.ResourceId, out var currentValue) &&
                    string.Equals(currentValue ?? string.Empty, change.Value ?? string.Empty, StringComparison.Ordinal))
                {
                    continue;
                }

                var requestedValue = change.Value ?? string.Empty;
                var mutation = new StateMutation
                {
                    Namespace = def.Namespace,
                    Key = def.Key,
                    OwnerKind = def.OwnerKind,
                    OwnerId = def.OwnerId,
                    Op = change.Op,
                    Value = requestedValue
                };
                var results = await _stateMutationService.ApplyAsync(gameId, [mutation]);
                var applyResult = results.FirstOrDefault();
                if (applyResult is null || !applyResult.Ok)
                {
                    _logger.LogWarning("Resource {ResourceId} apply via State API failed: {Error}",
                        change.ResourceId, applyResult?.Error ?? "unknown");
                    continue;
                }
                var newValue = applyResult.NewValue ?? requestedValue;
                var wasClamped = applyResult.Clamped;

                if (wasClamped)
                    _logger.LogInformation("Resource {ResourceId} clamped to bounds after apply", change.ResourceId);

                snapshotToUpdate[change.ResourceId] = newValue;
                valid.Add(new StateAppliedChange(
                    change.ResourceId,
                    def.Namespace,
                    change.Op,
                    applyResult.OldValue,
                    newValue));
            }
        }
        return valid.ToArray();
    }

    private static LlmEvaluationResponse? ParseEvaluationResponse(string raw)
    {
        try
        {
            var node = openLuo.Modules.Llm.Infrastructure.Chat.LlmCallHelper.ParseJsonResponse(raw)?.AsObject();
            if (node is null)
                return null;

            var changes = new List<StateChange>();
            if (node["resourceChanges"] is JsonArray items)
            {
                foreach (var item in items.OfType<JsonObject>())
                {
                    var resourceId = item["resourceId"]?.GetValue<string>()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(resourceId))
                        continue;

                    var op = item["op"]?.GetValue<string>()?.Trim() ?? "delta";
                    var value = item["value"]?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    changes.Add(new StateChange(resourceId, op, value));
                }
            }

            return new LlmEvaluationResponse(changes.ToArray(), node["reason"]?.GetValue<string>() ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private sealed record LlmEvaluationResponse(
        [property: JsonPropertyName("resourceChanges")] StateChange[] StateChanges,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record EvalDef(
        string ResourceId,
        string Namespace,
        string Key,
        StateOwnerKind OwnerKind,
        string OwnerId,
        bool MutableByLlm,
        bool Derived,
        string? PromptContext,
        double? MaxDeltaPerTurn,
        ResourceLifecycleState LifecycleState);

    private static EvalDef ToEvalDef(ResourceEvaluationItemView item) =>
        new(
            ResourceId: item.ResourceId,
            Namespace: item.Definition.Namespace,
            Key: item.Definition.Key,
            OwnerKind: item.Definition.OwnerKind,
            OwnerId: item.OwnerId,
            MutableByLlm: item.Definition.MutableByLlm,
            Derived: item.Definition.Derived,
            PromptContext: item.Definition.PromptContext,
            MaxDeltaPerTurn: item.MaxDeltaPerTurn,
            LifecycleState: item.Definition.LifecycleState);
}
