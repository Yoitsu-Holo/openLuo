namespace openLuo.Modules.Executor.Core.Models;

public sealed class StructuredParseResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string Json { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;

    public static StructuredParseResult<T> Ok(T value, string json) => new()
    {
        Success = true,
        Value = value,
        Json = json
    };

    public static StructuredParseResult<T> Fail(string error, string json = "") => new()
    {
        Success = false,
        Error = error,
        Json = json
    };
}
