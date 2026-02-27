namespace openLuo.Modules.GameBridge.Core.Attributes;

/// <summary>
/// Overrides the JSON parameter key for a method parameter.
/// When not present, the camelCase version of the parameter name is used.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromParamsAttribute : Attribute
{
    /// <summary>The JSON field name to bind from.</summary>
    public string? Key { get; init; }

    /// <summary>Optional default value if the key is missing from params.</summary>
    public string? DefaultValue { get; init; }
}
