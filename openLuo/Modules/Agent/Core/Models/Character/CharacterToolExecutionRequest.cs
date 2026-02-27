namespace openLuo.Modules.Agent.Application;

public sealed class CharacterToolExecutionRequest
{
    public IReadOnlyList<string> AllowedToolNames { get; init; } = [];
    public string LastToolResult { get; init; } = string.Empty;
    public int Iteration { get; init; } = 1;
}
