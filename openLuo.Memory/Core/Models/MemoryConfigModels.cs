namespace openLuo.Modules.Memory.Core.Models;

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

public class MemoryRetrievalConfig
{
    public int CharacterTopK { get; set; } = 8;
    public int GlobalTopK { get; set; } = 4;
    public int RecentN { get; set; } = 12;
    public int EmotionalN { get; set; } = 8;
    public double? GlobalDistanceMax { get; set; } = 0.7;

    public MemoryRetrievalConfig Clone() => new()
    {
        CharacterTopK = CharacterTopK,
        GlobalTopK = GlobalTopK,
        RecentN = RecentN,
        EmotionalN = EmotionalN,
        GlobalDistanceMax = GlobalDistanceMax
    };
}

public class MemoryStoreConfig
{
    public int EmbeddingOperationTimeoutSeconds { get; set; } = 8;
    public int CompressionLookbackDays { get; set; } = 30;
    public int CompressionCheckCount { get; set; } = 50;

    public MemoryStoreConfig Clone() => new()
    {
        EmbeddingOperationTimeoutSeconds = EmbeddingOperationTimeoutSeconds,
        CompressionLookbackDays = CompressionLookbackDays,
        CompressionCheckCount = CompressionCheckCount
    };
}
