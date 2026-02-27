namespace openLuo.Modules.Embedding.Core.Interfaces;

/// <summary>
/// Provides text embedding capability.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>Whether embedding service is enabled by configuration.</summary>
    bool Enabled { get; }

    /// <summary>Generate embedding vector for text.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
