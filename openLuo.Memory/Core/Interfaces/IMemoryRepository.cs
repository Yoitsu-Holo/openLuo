using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Memory.Core.Interfaces;

/// <summary>
/// Persistence boundary of the memory module.
/// Stores structured memory rows and vector rows, but does not decide retrieval strategy.
/// </summary>
public interface IMemoryRepository
{
    /// <summary>
    /// Store the structured semantic record into the primary memory table.
    /// </summary>
    Task StoreRecordAsync(MemoryRecord record, CancellationToken ct = default);

    /// <summary>
    /// Store the vector representation for a memory record into the vector index table.
    /// </summary>
    Task StoreEmbeddingAsync(MemoryRecord record, float[] embedding, CancellationToken ct = default);

    /// <summary>
    /// Read recent memories directly from persistence.
    /// This is a low-level storage view, not a semantic retrieval pipeline.
    /// </summary>
    /// <param name="userId">
    /// 非空时只返回该用户（或未分区 <c>user_id = ''</c>）的记忆；为空 = 不过滤，行为与引入用户维度前一致。
    /// </param>
    Task<IReadOnlyList<MemoryRecord>> QueryRecentAsync(
        string gameId,
        string? ownerCharacterId,
        IReadOnlyList<MemoryScope> scopes,
        int limit,
        string? userId = null,
        CancellationToken ct = default);
}
