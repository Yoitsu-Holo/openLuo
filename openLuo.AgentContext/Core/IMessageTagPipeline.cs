namespace openLuo.AgentContext.Core;

/// <summary>
/// 消息级标签渲染器（EnhanceChat，D43）。扩展注册自己的渲染器；
/// 内核保证白名单 + 输出剥离。
/// </summary>
public interface IMessageTagRenderer
{
    /// <summary>监听的元数据键（如 "type"）。</summary>
    string Key { get; }

    /// <summary>把元数据渲染为语义标签列表（如 ["[TYPE: card]"]）。</summary>
    IReadOnlyList<string> Render(IReadOnlyDictionary<string, string> metadata);
}

/// <summary>
/// 标签管道（D43）：白名单渲染 + 输出剥离。渲染发生在序列化点；
/// 输出侧剥离 [NAME: value] 标记，防模型复述。
/// </summary>
public interface IMessageTagPipeline
{
    void Register(IMessageTagRenderer renderer);
    IReadOnlyList<string> Render(IReadOnlyDictionary<string, string>? metadata);
    string Compose(string content, string? timeTag, IReadOnlyList<string>? tags);
    string Strip(string content);
}
