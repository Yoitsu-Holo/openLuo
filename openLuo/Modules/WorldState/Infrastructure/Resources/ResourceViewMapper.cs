using System.Globalization;
using System.Text.Json.Nodes;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.Resources;

internal static class ResourceViewMapper
{
    public static ResourceDefinitionView ToView(StateDef def)
    {
        var metadata = ParseMetadata(def.MetadataJson);
        var label = metadata?["name"]?.ToString();

        return new ResourceDefinitionView
        {
            DefinitionId = def.DefinitionId,
            Namespace = def.Namespace,
            Key = def.Key,
            OwnerKind = def.OwnerKind,
            ValueType = def.ValueType,
            DefaultValue = def.DefaultValue,
            MinValue = def.MinValue,
            MaxValue = def.MaxValue,
            Derived = def.Derived,
            MutableByLlm = def.MutableByLlm,
            HiddenFromStatus = def.HiddenFromStatus,
            StatusGroup = def.StatusGroup,
            StatusOrder = def.StatusOrder,
            DisplayFormat = def.DisplayFormat,
            PromptContext = def.PromptContext,
            PluginId = def.PluginId,
            LifecycleState = def.LifecycleState,
            RetirementPolicy = def.RetirementPolicy,
            SourceKind = def.SourceKind,
            SourceRef = def.SourceRef,
            Label = string.IsNullOrWhiteSpace(label) ? def.Key : label!,
            EnumValues = def.EnumValues.ToArray(),
            Metadata = metadata
        };
    }

    public static string FormatDisplayText(ResourceDefinitionView def, string value)
    {
        var text = string.IsNullOrWhiteSpace(def.DisplayFormat)
            ? "{value}"
            : def.DisplayFormat!;

        return text
            .Replace("{value}", value, StringComparison.Ordinal)
            .Replace("{max}", def.MaxValue ?? string.Empty, StringComparison.Ordinal)
            .Replace("{min}", def.MinValue ?? string.Empty, StringComparison.Ordinal);
    }

    public static string ResolveStatusType(ResourceDefinitionView def)
    {
        if (def.ValueType == StateValueType.Number && !string.IsNullOrWhiteSpace(def.MaxValue))
            return "bar";

        return "text";
    }

    public static string? NormalizeMaxValue(ResourceDefinitionView def) =>
        double.TryParse(def.MaxValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var max)
            ? max.ToString(CultureInfo.InvariantCulture)
            : def.MaxValue;

    private static JsonObject? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject;
        }
        catch
        {
            return null;
        }
    }
}
