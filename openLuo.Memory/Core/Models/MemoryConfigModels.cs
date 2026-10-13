namespace openLuo.Modules.Memory.Core.Models;

/// <summary>
/// sqlite-vec 扩展配置（**真值**：<c>DatabaseInitializer</c> 用它建 vec0 表，
/// <c>SqliteConnectionFactory</c> 用它加载扩展；改动需重启）。
/// </summary>
public class SqliteVecConfig
{
    public string ExtensionPath { get; set; } = string.Empty;
    public int VectorDimensions { get; set; } = 2560;

    public SqliteVecConfig Clone() => new()
    {
        ExtensionPath = ExtensionPath,
        VectorDimensions = VectorDimensions
    };
}

// 已删除（零消费的假旋钮，勿再加回而不接实现）：
//  - MemoryRetrievalConfig（characterTopK/globalTopK/recentN/emotionalN/globalDistanceMax）：
//    检索器只认 SemanticRecallQuery.TopK/Scopes，实际调用方是 extensions/memory（写死 TopK=3）；
//    "每 scope 一个 topK / 最近 N / 情绪 N / 距离阈值" 在当前检索管线里没有对应能力。
//  - MemoryStoreConfig（embeddingOperationTimeoutSeconds/compressionLookbackDays/compressionCheckCount）：
//    embedding 超时真值在 openLuo.Embedding 的 EmbeddingConfig.TimeoutSeconds；压缩功能在本仓未实现
//    （memories.is_compressed 恒写 0）。
// 召回参数的重新设计随"记忆召回质量"一起做（届时配置与实现同时落地）。
