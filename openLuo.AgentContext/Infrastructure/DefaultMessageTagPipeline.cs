using System.Text.RegularExpressions;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认标签管道（D43）：白名单渲染 + 输出剥离。
/// 渲染发生在序列化点；输出侧剥离 [NAME: value] 标记，防模型复述。
/// </summary>
public sealed class DefaultMessageTagPipeline : IMessageTagPipeline
{
    private readonly Dictionary<string, IMessageTagRenderer> _renderers =
        new(StringComparer.OrdinalIgnoreCase);

    // [NAME: value] 语义单标签协议
    private static readonly Regex TagRegex = new(
        @"\[\s*[A-Za-z_][A-Za-z0-9_]*\s*:\s*[^\]]+\]",
        RegexOptions.Compiled);

    public void Register(IMessageTagRenderer renderer)
    {
        if (string.IsNullOrWhiteSpace(renderer.Key))
            return;
        _renderers[renderer.Key] = renderer;
    }

    public IReadOnlyList<string> Render(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return [];

        var tags = new List<string>();
        foreach (var renderer in _renderers.Values)
        {
            if (!metadata.TryGetValue(renderer.Key, out _))
                continue;
            tags.AddRange(renderer.Render(metadata));
        }
        return tags;
    }

    public string Compose(string content, string? timeTag, IReadOnlyList<string>? tags)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(timeTag))
            parts.Add(timeTag);
        if (tags is { Count: > 0 })
            parts.AddRange(tags);

        if (parts.Count == 0)
            return content;

        return $"{string.Join(" ", parts)} {content}";
    }

    public string Strip(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;
        return TagRegex.Replace(content.Trim(), string.Empty).Trim();
    }
}

/// <summary>类型标签渲染器（TYPE: card / image / msg，对应旧 ChatTagType）。</summary>
public sealed class TypeTagRenderer : IMessageTagRenderer
{
    public string Key => "type";

    public IReadOnlyList<string> Render(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue("type", out var type) && !string.IsNullOrWhiteSpace(type)
            ? [$"[TYPE: {type.Trim().ToLowerInvariant()}]"]
            : [];
}
