namespace openLuo.Modules.Llm.Core.Models;

/// <summary>
/// Unified request options for LLM completions.
/// </summary>
public class RequiredLlmCapabilities
{
    public bool Vision { get; set; } = false;
    public bool JsonMode { get; set; } = false;
    public bool Streaming { get; set; } = false;

    public RequiredLlmCapabilities Clone() => new()
    {
        Vision = Vision,
        JsonMode = JsonMode,
        Streaming = Streaming
    };
}

public class LlmOptions
{
    public float? Temperature { get; set; } = 0.5f;
    public int? MaxTokens { get; set; } = 4096;
    public bool JsonMode { get; set; } = false;
    /// <summary>
    /// When true AND the model supports vision, ImageBlocks are serialized as image_url content parts.
    /// When false, ImageBlocks are serialized as text placeholders via ToString() (no image data on the wire).
    /// Defaults to false — executors must explicitly opt into multimodal image recognition.
    /// </summary>
    public bool EnableMultimodal { get; set; } = false;
    /// <summary>
    /// Function-tool declarations sent in the <c>tools</c> array. When null, no tools array
    /// is emitted and the model is free to reply with plain text.
    /// </summary>
    public IReadOnlyList<LlmToolSpec>? Tools { get; set; }
    public RequiredLlmCapabilities RequiredCapabilities { get; set; } = new();
    public Dictionary<string, object?> ExtraBody { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
