namespace openLuo.Core.Models;

/// <summary>
/// A structured message containing one or more <see cref="Block"/>s.
/// This is the universal content exchange format across the platform —
/// from input to output, within Agent flows, and between frontend and backend.
/// </summary>
public sealed class Message
{
    public static Message Empty { get; } = new() { MessageId = string.Empty };

    public required string MessageId { get; init; }

    public string SpeakerRole { get; init; } = "assistant";

    public string? SpeakerId { get; init; }

    public OutputVisibility Visibility { get; init; } = OutputVisibility.Public;

    public IReadOnlyList<Block> Blocks { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Create a single-block text message.
    /// </summary>
    public static Message FromText(string text, string speakerRole = "assistant", string? speakerId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        return new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SpeakerRole = speakerRole,
            SpeakerId = speakerId,
            Blocks =
            [
                new TextBlock
                {
                    Kind = BlockKind.Text,
                    Text = text
                }
            ]
        };
    }

    /// <summary>
    /// Degrade the message to a plain-text string (text blocks joined by newlines).
    /// Useful for logging, storage, and LLM prompt construction.
    /// </summary>
    public string ToPlainText()
    {
        if (Blocks.Count == 0)
            return string.Empty;

        return string.Join(
            "\n",
            Blocks
                .OfType<TextBlock>()
                .Select(b => b.Text)
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }
}
