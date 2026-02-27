using openLuo.Modules.Embedding.Core.Interfaces;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.Memory.Core.Models;
using openLuo.Core.Interfaces;

namespace openLuo.Modules.Memory.Application;

/// <summary>
/// Application-layer write coordinator of the memory module.
/// Responsible for projection, primary record persistence, and optional embedding persistence.
/// </summary>
public sealed class MemoryCommitCoordinator : IMemoryWriteService
{
    private readonly IMemoryWriteProjector _projector;
    private readonly IMemoryRepository _repository;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IGameLogger? _logger;

    public MemoryCommitCoordinator(
        IMemoryWriteProjector projector,
        IMemoryRepository repository,
        IEmbeddingClient embeddingClient,
        IGameLogger? logger = null)
    {
        _projector = projector;
        _repository = repository;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task<MemoryWriteResult> WriteAsync(MemoryWriteInput input, CancellationToken ct = default)
    {
        var record = await _projector.ProjectAsync(input, ct);
        var trace = new List<string> { "projected:ok" };

        await _repository.StoreRecordAsync(record, ct);
        trace.Add("store:ok");

        if (_embeddingClient.Enabled)
        {
            try
            {
                // Prefer the retrieval-oriented field for vectorization, then fall back to
                // the compact summary, and finally the raw source text.
                var vectorText = !string.IsNullOrWhiteSpace(record.RecallText)
                    ? record.RecallText
                    : !string.IsNullOrWhiteSpace(record.Summary)
                        ? record.Summary
                        : record.SourceText;
                var embedding = await _embeddingClient.EmbedAsync(vectorText, ct);
                await _repository.StoreEmbeddingAsync(record, embedding, ct);
                trace.Add("embedding:ok");
            }
            catch (Exception ex)
            {
                _logger?.Warn("memory", $"MemoryCommitCoordinator embedding skipped memoryId={record.Id}: {ex.Message}");
                trace.Add("embedding:skipped");
            }
        }
        else
        {
            trace.Add("embedding:disabled");
        }

        return new MemoryWriteResult
        {
            Success = true,
            MemoryId = record.Id,
            StoredScope = record.Scope,
            StoredAtUtc = DateTime.UtcNow,
            Trace = trace
        };
    }
}
