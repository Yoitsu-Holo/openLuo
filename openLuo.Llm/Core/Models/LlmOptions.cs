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
    /// <summary>
    /// 采样温度。<c>null</c> = 调用方未指定 → 由 route 配置（llm.jsonc 的 temperature）决定；
    /// 显式赋值（含 0f）才覆盖 route。绝不在这里写硬编码默认值：
    /// 一旦非 null，LlmClientBase.MergeOptions 的 `?? route 值` 就永远命中不到 route 配置。
    /// </summary>
    public float? Temperature { get; set; } = null;

    /// <summary>
    /// 输出 token 上限。语义同 <see cref="Temperature"/>：<c>null</c> = 不指定 →
    /// route 配置决定；route 也为 null 时请求体里不出现 max_tokens（表示“用服务端默认”），
    /// 而不是注入一个硬编码上限。
    /// </summary>
    public int? MaxTokens { get; set; } = null;

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
