using openLuo.Core;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.State;

public class StateMutationService(IStateRegistry registry, IStateStore store) : IStateMutationService
{
    private static string RelationshipStageLabel(RelationshipStage stage) => stage switch
    {
        RelationshipStage.Stranger => "陌生人",
        RelationshipStage.Acquaintance => "熟人",
        RelationshipStage.Friend => "朋友",
        RelationshipStage.CloseFriend => "好友",
        RelationshipStage.Lover => "恋人",
        _ => "陌生人"
    };

    public async Task<List<StateMutationResult>> ApplyAsync(string gameId, IEnumerable<StateMutation> mutations)
    {
        var results = new List<StateMutationResult>();

        foreach (var mutation in mutations)
        {
            var result = new StateMutationResult
            {
                Namespace = mutation.Namespace,
                Key = mutation.Key,
                OwnerKind = mutation.OwnerKind,
                OwnerId = mutation.OwnerId
            };

            try
            {
                var def = registry.GetDef(mutation.Namespace, mutation.OwnerKind, mutation.Key);
                if (def is null)
                {
                    result.Ok = false;
                    result.Error = "state_not_defined";
                    results.Add(result);
                    continue;
                }

                if (def.Derived)
                {
                    result.Ok = false;
                    result.Error = "derived_state_not_writable";
                    results.Add(result);
                    continue;
                }

                if (def.LifecycleState is ResourceLifecycleState.Frozen or ResourceLifecycleState.Retired)
                {
                    result.Ok = false;
                    result.Error = $"resource_{def.LifecycleState.ToString().ToLowerInvariant()}";
                    results.Add(result);
                    continue;
                }

                var rawCurrent = await store.GetRawAsync(gameId, mutation.OwnerKind, mutation.OwnerId, mutation.Namespace, mutation.Key);
                var currentValue = rawCurrent ?? def.DefaultValue ?? "0";
                result.OldValue = currentValue;

                string newValue;
                bool clamped = false;

                if (def.ValueType == StateValueType.Number)
                {
                    double numericCurrent = double.TryParse(currentValue, out var c) ? c : 0.0;
                    double numericResult;

                    if (mutation.Op == "delta")
                    {
                        var delta = double.TryParse(mutation.Value, out var d) ? d : 0.0;
                        numericResult = numericCurrent + delta;
                    }
                    else // "set"
                    {
                        numericResult = double.TryParse(mutation.Value, out var s) ? s : 0.0;
                    }

                    if (def.MinValue is not null && double.TryParse(def.MinValue, out var minVal) && numericResult < minVal)
                    {
                        numericResult = minVal;
                        clamped = true;
                    }
                    if (def.MaxValue is not null && double.TryParse(def.MaxValue, out var maxVal) && numericResult > maxVal)
                    {
                        numericResult = maxVal;
                        clamped = true;
                    }

                    newValue = numericResult == Math.Floor(numericResult)
                        ? ((long)numericResult).ToString()
                        : numericResult.ToString("G");
                }
                else if (def.ValueType == StateValueType.Enum)
                {
                    newValue = mutation.Value;
                    if (def.EnumValues.Count > 0 && !def.EnumValues.Contains(mutation.Value, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Ok = false;
                        result.Error = $"invalid_enum_value:{mutation.Value}";
                        results.Add(result);
                        continue;
                    }
                }
                else
                {
                    newValue = mutation.Value;
                }

                await store.SetAsync(gameId, mutation.OwnerKind, mutation.OwnerId, mutation.Namespace, mutation.Key, newValue);
                await store.LogChangeAsync(gameId, mutation.OwnerKind, mutation.OwnerId,
                    mutation.Namespace, mutation.Key,
                    oldValue: currentValue, newValue: newValue,
                    changeType: mutation.Op,
                    reason: mutation.Reason,
                    sourceType: mutation.SourceType,
                    sourceId: mutation.SourceId);

                result.NewValue = newValue;
                result.Clamped = clamped;
                result.Ok = true;

                if (mutation.Namespace.Equals("char_status", StringComparison.OrdinalIgnoreCase)
                    && mutation.Key.Equals("affection", StringComparison.OrdinalIgnoreCase))
                {
                    var relationshipStageDef = registry.GetDef("char_status", mutation.OwnerKind, "relationship_stage");
                    if (relationshipStageDef?.Derived == true)
                    {
                        var affection = int.TryParse(newValue, out var affectionInt)
                            ? affectionInt
                            : (int)(double.TryParse(newValue, out var affectionDouble) ? affectionDouble : 0d);
                        var derivedStage = RelationshipStageLabel(GameConstants.GetRelationshipStageForAffection(affection));
                        var previousStage = await store.GetRawAsync(gameId, mutation.OwnerKind, mutation.OwnerId, "char_status", "relationship_stage")
                                           ?? relationshipStageDef.DefaultValue
                                           ?? "陌生人";

                        await store.SetAsync(gameId, mutation.OwnerKind, mutation.OwnerId, "char_status", "relationship_stage", derivedStage);
                        await store.LogChangeAsync(gameId, mutation.OwnerKind, mutation.OwnerId,
                            "char_status", "relationship_stage",
                            oldValue: previousStage,
                            newValue: derivedStage,
                            changeType: "derive",
                            reason: "auto_derived_from_affection",
                            sourceType: "state_derived",
                            sourceId: "char_status.affection");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
            }

            results.Add(result);
        }

        return results;
    }
}
