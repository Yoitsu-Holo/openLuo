namespace openLuo.Modules.Agent.Application;

public static class CharacterTurnTraceBuilder
{
    public static IReadOnlyList<string> Build(IReadOnlyList<AgentToolUseStep> steps)
    {
        return steps.Select(step => $"[{step.Action}] {step.Name} => {(step.Success ? "ok" : "fail")} {step.Summary}").ToList();
    }
}
