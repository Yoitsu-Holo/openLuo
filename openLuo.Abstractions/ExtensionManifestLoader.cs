using System.Text.Json;
using System.Text.Json.Nodes;

namespace openLuo.Abstractions;

/// <summary>
/// manifest 加载与校验（D26/D27/D28/D24）。
/// 只处理单个 extension.jsonc 的解析；目录扫描在 ExtensionHost。
/// </summary>
public static class ExtensionManifestLoader
{
    public static ExtensionManifest? Load(string extensionJsoncPath)
    {
        try
        {
            var json = File.ReadAllText(extensionJsoncPath);
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (node is null)
                return null;

            var manifest = node.Deserialize<ExtensionManifest>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                return null;

            return manifest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>基础校验：id/version 合法、assembly/entryType 非空。</summary>
    public static bool IsValid(ExtensionManifest manifest) =>
        !string.IsNullOrWhiteSpace(manifest.Id)
        && !string.IsNullOrWhiteSpace(manifest.Version)
        && !string.IsNullOrWhiteSpace(manifest.Assembly)
        && !string.IsNullOrWhiteSpace(manifest.EntryType);
}
