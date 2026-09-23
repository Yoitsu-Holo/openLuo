namespace openLuo.Abstractions;

/// <summary>扩展依赖声明（minVersion，D27）。</summary>
public sealed class ExtensionDependency
{
    public string Id { get; init; } = string.Empty;
    public string MinVersion { get; init; } = "0.0.0";
}

/// <summary>
/// extension.jsonc manifest（D26/D27/D28）。
/// Id/Version 为单一事实源；entryType 为完整类型名；dataDir 相对扩展根目录。
/// </summary>
public sealed class ExtensionManifest
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Assembly { get; init; } = string.Empty;
    public string EntryType { get; init; } = string.Empty;
    public IReadOnlyList<ExtensionDependency> Requires { get; init; } = [];
    public string DataDir { get; init; } = "data";
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>扩展目录（扫描结果）。</summary>
public sealed class ExtensionDirectory
{
    public string DirectoryPath { get; init; } = string.Empty;
    public ExtensionManifest Manifest { get; init; } = new();
}
