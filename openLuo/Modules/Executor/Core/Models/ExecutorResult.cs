namespace openLuo.Modules.Executor.Core.Models;

public sealed class ExecutorResult<TOutput>
{
    public bool Success { get; init; }
    public TOutput? Output { get; init; }
    public string Error { get; init; } = string.Empty;
    public string RawOutput { get; init; } = string.Empty;

    public static ExecutorResult<TOutput> Ok(TOutput output, string rawOutput = "") => new()
    {
        Success = true,
        Output = output,
        RawOutput = rawOutput
    };

    public static ExecutorResult<TOutput> Fail(string error, string rawOutput = "") => new()
    {
        Success = false,
        Error = error,
        RawOutput = rawOutput
    };
}
