using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Infrastructure.Retrieval;

/// <summary>
/// Retrieves via the preferred vector path first and falls back to lexical retrieval when needed.
/// </summary>
public sealed class CompositeMemoryRetriever : IMemoryRetriever
{
    private readonly IMemoryRetriever _vectorRetriever;
    private readonly IMemoryRetriever _keywordRetriever;

    public CompositeMemoryRetriever(
        VectorMemoryRetriever vectorRetriever,
        KeywordMemoryRetriever keywordRetriever)
    {
        _vectorRetriever = vectorRetriever;
        _keywordRetriever = keywordRetriever;
    }

    public async Task<MemoryRecallResult> RetrieveAsync(SemanticRecallQuery query, CancellationToken ct = default)
    {
        MemoryRecallResult vectorResult;
        try
        {
            vectorResult = await _vectorRetriever.RetrieveAsync(query, ct);
            if (vectorResult.Success && vectorResult.Records.Count > 0)
                return vectorResult;
        }
        catch (Exception ex)
        {
            // Retrieval should degrade, not crash the caller.
            vectorResult = new MemoryRecallResult
            {
                Success = false,
                Trace =
                [
                    "retriever:vector",
                    $"vectorErrorType:{ex.GetType().Name}",
                    $"vectorErrorMessage:{ClipTraceValue(ex.Message)}"
                ],
                Degraded = true
            };
        }

        var keywordResult = await _keywordRetriever.RetrieveAsync(query, ct);
        return new MemoryRecallResult
        {
            Success = keywordResult.Success,
            Records = keywordResult.Records,
            Summary = keywordResult.Summary,
            Trace = vectorResult.Trace.Concat(keywordResult.Trace).ToArray(),
            Degraded = true
        };
    }

    private static string ClipTraceValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<empty>";

        var singleLine = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= 160 ? singleLine : singleLine[..160] + "...";
    }
}
