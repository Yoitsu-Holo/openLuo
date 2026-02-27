using System.Text;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Executor.Application.MemoryRecall;

public sealed class DefaultMemoryRecallFormatter : IMemoryRecallFormatter
{
    public MemoryRecallFormattedResult Format(
        IReadOnlyList<MemoryRecord> records,
        MemoryRecallOptions options)
    {
        var selected = records
            .OrderByDescending(static r => r.Importance)
            .ThenByDescending(static r => r.Salience)
            .ThenByDescending(static r => r.OccurredAtUtc)
            .Take(options.MaxSnippetCount)
            .ToList();

        var snippets = selected.Select(static record => new MemorySnippet
        {
            MemoryId = record.Id,
            Summary = string.IsNullOrWhiteSpace(record.Summary) ? record.RecallText : record.Summary,
            Tags = record.Tags,
            Scope = record.Scope,
            Score = record.Salience
        }).ToArray();

        var summaryBuilder = new StringBuilder();
        foreach (var snippet in snippets)
        {
            if (summaryBuilder.Length > 0)
                summaryBuilder.Append('\n');

            var scopeLabel = snippet.Scope == MemoryScope.Shared ? "shared" : "private";
            summaryBuilder.Append("- [");
            summaryBuilder.Append(scopeLabel);
            summaryBuilder.Append("] ");
            summaryBuilder.Append(snippet.Summary.Trim());

            if (snippet.Tags.Count > 0)
            {
                summaryBuilder.Append(" | tags=");
                summaryBuilder.Append(string.Join(", ", snippet.Tags));
            }

            if (summaryBuilder.Length >= options.MaxSummaryChars)
                break;
        }

        var summary = summaryBuilder.ToString();
        if (summary.Length > options.MaxSummaryChars)
            summary = summary[..options.MaxSummaryChars].TrimEnd();

        return new MemoryRecallFormattedResult
        {
            Snippets = snippets,
            Summary = summary
        };
    }
}
