using System.Text.Json.Serialization;
using openLuo.Core.Models;

namespace openLuo.Modules.Llm.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ChatMessageRole>))]
public enum ChatMessageRole
{
    [JsonStringEnumMemberName("system")] System,
    [JsonStringEnumMemberName("user")] User,
    [JsonStringEnumMemberName("assistant")] Assistant,
    [JsonStringEnumMemberName("tool")] Tool
}

/// <summary>
/// Represents a single message in a chat conversation.
/// Supports both plain-text (<see cref="Content"/>) and multi-modal (<see cref="Blocks"/>) content.
/// </summary>
public class ChatMessage
{
    public ChatMessageRole Role { get; init; }

    /// <summary>
    /// Plain-text content. When <see cref="Blocks"/> is non-empty, this is derived from
    /// the text blocks via <see cref="Message.ToPlainText"/> (non-text blocks are skipped).
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Multi-modal content blocks (TextBlock, ImageBlock, AssetBlock, AudioBlock, etc.).
    /// When empty, <see cref="Content"/> is used as the sole text payload.
    /// </summary>
    public IReadOnlyList<Block>? Blocks { get; init; }

    public ChatMessage() { }

    public ChatMessage(ChatMessageRole role, string content)
    {
        Role = role;
        Content = content;
    }

    public ChatMessage(ChatMessageRole role, IReadOnlyList<Block> blocks)
    {
        Role = role;
        Blocks = blocks;
        Content = ToPlainText(blocks);
    }

    private static string ToPlainText(IReadOnlyList<Block> blocks)
    {
        if (blocks is null || blocks.Count == 0)
            return string.Empty;

        return string.Join(
            "\n",
            blocks
                .OfType<TextBlock>()
                .Select(b => b.Text)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }
}
