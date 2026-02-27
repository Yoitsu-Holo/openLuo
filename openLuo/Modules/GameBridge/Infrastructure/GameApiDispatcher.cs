using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using openLuo.Modules.GameBridge.Core.Attributes;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.GameBridge.Infrastructure;

/// <summary>
/// Scans [GameApi] methods on a target type at startup and dispatches
/// JSON-RPC calls to them at runtime. Eliminates the need for a manual
/// switch statement over game/* routes.
/// </summary>
public sealed class GameApiDispatcher
{
    private readonly Dictionary<string, RouteEntry> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _routeTargetMap = new(StringComparer.Ordinal);
    private readonly Type[] _targetTypes;

    public GameApiDispatcher(params Type[] targetTypes)
    {
        _targetTypes = targetTypes;
        ScanRoutes();
    }

    /// <summary>All registered routes for introspection / help generation.</summary>
    public IReadOnlyDictionary<string, RouteEntry> Routes => _routes;

    // ── Scanning ──────────────────────────────────────────────────────────────

    private void ScanRoutes()
    {
        foreach (var targetType in _targetTypes)
        {
            foreach (var method in targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetCustomAttribute<GameApiAttribute>();
                if (attr is null) continue;

                if (_routes.ContainsKey(attr.Route))
                    throw new InvalidOperationException($"Duplicate GameApi route: {attr.Route}");

                var parameters = method.GetParameters();
                var gameIdIndex = FindGameIdParameter(parameters);
                var ctIndex = FindCancellationTokenParameter(parameters);
                var paramBindings = BuildParamBindings(parameters, gameIdIndex, ctIndex);

                _routes[attr.Route] = new RouteEntry
                {
                    Route = attr.Route,
                    Description = attr.Description,
                    Method = method,
                    Parameters = parameters,
                    GameIdParamIndex = gameIdIndex,
                    CancellationTokenParamIndex = ctIndex,
                    ParamBindings = paramBindings
                };
                _routeTargetMap[attr.Route] = targetType;
            }
        }
    }

    private static int FindGameIdParameter(ParameterInfo[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == "gameId" && parameters[i].ParameterType == typeof(string))
                return i;
        }
        return -1;
    }

    private static int FindCancellationTokenParameter(ParameterInfo[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
                return i;
        }
        return -1;
    }

    private static ParamBinding[] BuildParamBindings(ParameterInfo[] parameters, int gameIdIndex, int ctIndex)
    {
        var bindings = new ParamBinding[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i == gameIdIndex || i == ctIndex) continue;

            var param = parameters[i];
            var fromParams = param.GetCustomAttribute<FromParamsAttribute>();
            bindings[i] = new ParamBinding
            {
                Parameter = param,
                JsonKey = fromParams?.Key ?? ToCamelCase(param.Name!),
                DefaultValue = fromParams?.DefaultValue
            };
        }
        return bindings;
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    public async Task<object?> DispatchAsync(
        string route,
        JsonNode? paramsNode,
        GameBridgeRequestContext? bridgeContext,
        IServiceProvider services,
        CancellationToken ct)
    {
        if (!_routes.TryGetValue(route, out var entry))
            throw new NotSupportedException($"未注册的 GameApi 路由: {route}");

        var targetType = _routeTargetMap[route];
        var instance = services.GetRequiredService(targetType);
        var argValues = new object?[entry.Parameters.Length];
        var paramDict = DeserializeParams(paramsNode);

        for (int i = 0; i < entry.Parameters.Length; i++)
        {
            if (i == entry.GameIdParamIndex)
            {
                argValues[i] = ResolveGameId(bridgeContext, entry.Parameters[i]);
            }
            else if (i == entry.CancellationTokenParamIndex)
            {
                argValues[i] = ct;
            }
            else
            {
                var binding = entry.ParamBindings[i];
                argValues[i] = BindParameter(binding, paramDict);
            }
        }

        var result = entry.Method.Invoke(instance, argValues);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            // Extract the result from Task<T>
            var taskType = task.GetType();
            if (taskType.IsGenericType)
                return taskType.GetProperty("Result")!.GetValue(task);
            return null;
        }
        return result;
    }

    // ── gameId resolution ─────────────────────────────────────────────────────

    private static string? ResolveGameId(GameBridgeRequestContext? ctx, ParameterInfo param)
    {
        var gameId = ctx?.GameId;
        if (string.IsNullOrWhiteSpace(gameId) && !param.IsOptional)
            throw new InvalidOperationException(
                $"参数 '{param.Name}' 需要 gameId，但 bridgeContext 中未提供");
        return gameId;
    }

    // ── Parameter binding ─────────────────────────────────────────────────────

    private static object? BindParameter(ParamBinding binding, Dictionary<string, JsonElement?> paramDict)
    {
        var param = binding.Parameter;
        var hasValue = paramDict.TryGetValue(binding.JsonKey, out var jsonElement);

        // If no value, use default or null
        if (!hasValue || jsonElement is null)
        {
            if (!string.IsNullOrEmpty(binding.DefaultValue))
                return ConvertValue(binding.DefaultValue, param.ParameterType);
            if (param.IsOptional)
                return param.DefaultValue;
            return param.ParameterType.IsValueType
                ? Activator.CreateInstance(param.ParameterType)
                : null;
        }

        // Pass raw JsonNode for JsonNode? parameters without converting
        if (param.ParameterType == typeof(JsonNode))
        {
            if (hasValue && jsonElement.HasValue)
            {
                var rawText = jsonElement.Value.GetRawText();
                return JsonNode.Parse(rawText);
            }
            return null;
        }

        return ConvertJsonElement(jsonElement.Value, param.ParameterType);
    }

    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlying == typeof(string))
            {
                return element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.GetRawText();
            }
            if (underlying == typeof(int))
                return element.ValueKind == JsonValueKind.Number ? element.GetInt32() : int.TryParse(element.GetRawText().Trim('"'), out var iv) ? iv : 0;
            if (underlying == typeof(long))
                return element.ValueKind == JsonValueKind.Number ? element.GetInt64() : long.TryParse(element.GetRawText().Trim('"'), out var lv) ? lv : 0L;
            if (underlying == typeof(double))
                return element.ValueKind == JsonValueKind.Number ? element.GetDouble() : double.TryParse(element.GetRawText().Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out var dv) ? dv : 0.0;
            if (underlying == typeof(bool))
                return element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : element.GetRawText().Trim('"').ToLowerInvariant() is "true";
            if (underlying == typeof(IReadOnlyList<string>) || underlying == typeof(string[]))
                return DeserializeStringArray(element);
            if (underlying == typeof(IReadOnlyList<JsonObject>) || underlying == typeof(JsonArray))
                return element.ValueKind == JsonValueKind.Array
                    ? JsonNode.Parse(element.GetRawText())?.AsArray()
                    : null;

            // Fallback: deserialize to target type
            var text = element.GetRawText();
            return JsonSerializer.Deserialize(text, targetType, _jsonOpts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GameApiDispatcher] Failed to convert JSON value to {targetType.Name}: {ex.Message}");
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }
    }

    private static string[]? DeserializeStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return null;
        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                items.Add(item.GetString()!);
            else
                items.Add(item.GetRawText());
        }
        return items.ToArray();
    }

    private static object ConvertValue(string raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return underlying switch
        {
            _ when underlying == typeof(string) => raw,
            _ when underlying == typeof(int) => int.TryParse(raw, out var i) ? i : 0,
            _ when underlying == typeof(long) => long.TryParse(raw, out var l) ? l : 0L,
            _ when underlying == typeof(double) => double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0.0,
            _ when underlying == typeof(bool) => bool.TryParse(raw, out var b) ? b : raw.ToLowerInvariant() is "true",
            _ => raw
        };
    }

    private static Dictionary<string, JsonElement?> DeserializeParams(JsonNode? paramsNode)
    {
        if (paramsNode is null) return new();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement?>>(paramsNode, _jsonOpts)
                ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ── Route entry ───────────────────────────────────────────────────────────

    public sealed class RouteEntry
    {
        public required string Route { get; init; }
        public string? Description { get; init; }
        public required MethodInfo Method { get; init; }
        public required ParameterInfo[] Parameters { get; init; }
        public int GameIdParamIndex { get; init; } = -1;
        public int CancellationTokenParamIndex { get; init; } = -1;
        public required ParamBinding[] ParamBindings { get; init; }
    }

    public sealed class ParamBinding
    {
        public required ParameterInfo Parameter { get; init; }
        public required string JsonKey { get; init; }
        public string? DefaultValue { get; init; }
    }
}
