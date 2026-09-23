namespace openLuo.Application.Models.StateEvaluation;

/// <summary>
/// Prompt fragment from plugin hook
/// </summary>
public record PromptFragment(
    string Phase,
    int Priority,
    string Text);
