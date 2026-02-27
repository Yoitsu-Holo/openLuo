namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// Unified request options for LLM completions.
/// </summary>
public class LlmOptions
{
    public float? Temperature { get; set; } = 0.5f;
    public int? MaxTokens { get; set; } =4096;
    public bool JsonMode { get; set; } = false;
    public Dictionary<string, object?> ExtraBody { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
