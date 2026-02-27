namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionExecutionRequest
{
    public required string RawInput { get; init; }

    public required SessionExecutionContext Context { get; init; }
}
