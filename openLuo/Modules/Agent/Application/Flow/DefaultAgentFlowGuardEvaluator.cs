using System.Reflection;
using openLuo.Modules.Agent.Core.Interfaces.Flow;
using openLuo.Modules.Agent.Core.Models.Flow;

namespace openLuo.Modules.Agent.Application;

public sealed class DefaultAgentFlowGuardEvaluator : IAgentFlowGuardEvaluator
{
    public bool Allows(AgentFlowGuard guard, AgentFlowRunRequest request, IReadOnlyDictionary<string, object?> state)
    {
        return guard.Kind switch
        {
            AgentFlowGuardKind.None => true,
            AgentFlowGuardKind.OutputExists => ResolveValue(state, guard.Key) is not null,
            AgentFlowGuardKind.OutputEquals => ValueEquals(ResolveValue(state, guard.Key), guard.Value),
            _ => true
        };
    }

    private static bool ValueEquals(object? actual, string expected)
    {
        if (actual is null)
            return false;

        if (actual is bool boolValue && bool.TryParse(expected, out var expectedBool))
            return boolValue == expectedBool;

        if (actual is string stringValue)
            return string.Equals(stringValue, expected, StringComparison.OrdinalIgnoreCase);

        return string.Equals(
            Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static object? ResolveValue(IReadOnlyDictionary<string, object?> state, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || !state.TryGetValue(segments[0], out var current))
            return null;

        for (var i = 1; i < segments.Length; i++)
        {
            if (current is null)
                return null;

            if (current is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(segments[i], out var dictValue))
            {
                current = dictValue;
                continue;
            }

            var prop = current.GetType().GetProperty(
                segments[i],
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (prop is null)
                return null;

            current = prop.GetValue(current);
        }

        return current;
    }
}
