using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Application;

/// <summary>
/// Minimal default projector that translates raw write input into a structured memory record.
/// It intentionally keeps the logic simple: preserve source text, build a compact summary, and derive lightweight tags.
/// </summary>
public sealed class DefaultMemoryWriteProjector : IMemoryWriteProjector
{
    /// <summary>
    /// Convert the caller-facing write input into the canonical structured memory record.
    /// </summary>
    public Task<MemoryRecord> ProjectAsync(MemoryWriteInput input, CancellationToken ct = default)
    {
        var content = input.RawContent.Trim();
        var summary = content.Length <= 160 ? content : $"{content[..157]}...";
        var tags = BuildTags(content);

        return Task.FromResult(new MemoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GameId = input.GameId,
            OwnerCharacterId = input.CharacterId,
            Scope = input.Scope,
            OccurredAtUtc = input.OccurredAtUtc,
            SourceText = content,
            Summary = summary,
            RecallText = string.Join(' ', tags.Prepend(summary).Where(static s => !string.IsNullOrWhiteSpace(s))),
            Tags = tags,
            Emotion = input.Emotion,
            Importance = Math.Clamp(input.Importance, 0.0f, 1.0f),
            Salience = Math.Clamp(input.Importance, 0.0f, 1.0f),
            Metadata = input.Metadata
        });
    }

    private static IReadOnlyList<string> BuildTags(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        // Keep the default tagger deliberately conservative.
        // It is only a bootstrap heuristic until a richer projector is introduced.
        return content
            .Split([' ', '\n', '\r', '\t', '，', '。', '：', ':', '、', ',', '！', '？', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => token.Trim())
            .Where(static token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }
}
