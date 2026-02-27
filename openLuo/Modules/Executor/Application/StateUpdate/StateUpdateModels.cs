using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace openLuo.Modules.Executor.Application.StateUpdate;

public sealed class StateUpdateInput
{
    public string? SystemPromptOverride { get; init; }
    public string CurrentStateSummary { get; init; } = string.Empty;
    public string SceneState { get; init; } = string.Empty;
    public string PlayerInput { get; init; } = string.Empty;
    public string CharacterResponse { get; init; } = string.Empty;
    public IReadOnlyList<string> ToolResults { get; init; } = [];
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed class StateUpdateOutput
{
    [JsonPropertyName("deltas")]
    public StateDelta[] Deltas { get; init; } = [];

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

public sealed class StateDelta
{
    [JsonPropertyName("resourceId")]
    public string ResourceId { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public StateScalarValue Value { get; init; } = StateScalarValue.Null;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

[JsonConverter(typeof(StateScalarValueJsonConverter))]
public readonly struct StateScalarValue
{
    private readonly object? _value;

    private StateScalarValue(object? value)
    {
        _value = value;
    }

    public static StateScalarValue Null => new(null);

    public bool HasValue => _value is not null;

    public object? RawValue => _value;

    public bool IsString => _value is string;
    public bool IsBoolean => _value is bool;
    public bool IsInteger => _value is int;
    public bool IsFloat => _value is double;

    public string? AsString() => _value as string;
    public bool? AsBoolean() => _value as bool?;
    public int? AsInteger() => _value as int?;
    public double? AsFloat() => _value switch
    {
        double d => d,
        int i => i,
        _ => null
    };

    public static StateScalarValue FromString(string value) => new(value);
    public static StateScalarValue FromBoolean(bool value) => new(value);
    public static StateScalarValue FromInteger(int value) => new(value);
    public static StateScalarValue FromFloat(double value) => new(value);

    public override string ToString() =>
        _value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            string s => s,
            _ => Convert.ToString(_value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    public static implicit operator StateScalarValue(string value) => FromString(value);
    public static implicit operator StateScalarValue(bool value) => FromBoolean(value);
    public static implicit operator StateScalarValue(int value) => FromInteger(value);
    public static implicit operator StateScalarValue(double value) => FromFloat(value);
}

public sealed class StateScalarValueJsonConverter : JsonConverter<StateScalarValue>
{
    public override StateScalarValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => StateScalarValue.FromString(reader.GetString() ?? string.Empty),
            JsonTokenType.True => StateScalarValue.FromBoolean(true),
            JsonTokenType.False => StateScalarValue.FromBoolean(false),
            JsonTokenType.Number when reader.TryGetInt32(out var intValue) => StateScalarValue.FromInteger(intValue),
            JsonTokenType.Number => StateScalarValue.FromFloat(reader.GetDouble()),
            JsonTokenType.Null => StateScalarValue.Null,
            _ => throw new JsonException($"Unsupported state scalar token: {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, StateScalarValue value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.IsString)
        {
            writer.WriteStringValue(value.AsString());
            return;
        }

        if (value.IsBoolean)
        {
            writer.WriteBooleanValue(value.AsBoolean() ?? false);
            return;
        }

        if (value.IsInteger)
        {
            writer.WriteNumberValue(value.AsInteger() ?? 0);
            return;
        }

        if (value.IsFloat)
        {
            writer.WriteNumberValue(value.AsFloat() ?? 0);
            return;
        }

        throw new JsonException("Unsupported state scalar value.");
    }
}
