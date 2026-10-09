using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Capabilities.Core;

namespace openLuo.Modules.AppShell.Application;

/// <summary>
/// <see cref="IConfigService"/> 的文件实现：命名空间对应 <c>{dir}/{ns}.jsonc</c>；
/// 运行时覆盖保存在进程内（可选落盘）。.jsonc 允许注释（<see cref="JsonCommentHandling.Skip"/>）。
/// </summary>
public sealed class JsonConfigService : IConfigService
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _dir;
    private readonly ConcurrentDictionary<string, JsonNode> _runtime = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _updated = new(StringComparer.OrdinalIgnoreCase);

    public JsonConfigService(string configDir) => _dir = configDir;

    public IReadOnlyList<ConfigNamespaceInfo> ListNamespaces()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_dir))
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*.jsonc"))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }
        foreach (var key in _runtime.Keys)
            names.Add(key);

        return names
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new ConfigNamespaceInfo(
                n,
                SourceOf(n),
                _runtime.ContainsKey(n) || File.Exists(PathOf(n)),
                _updated.TryGetValue(n, out var t) ? t : DateTimeOffset.MinValue))
            .ToList();
    }

    public async Task<ConfigNamespaceView?> GetAsync(string ns, CancellationToken ct = default)
    {
        if (!Exists(ns))
            return null;

        var file = await ReadFileAsync(ns, ct).ConfigureAwait(false);
        var overlay = _runtime.TryGetValue(ns, out var r) ? r.DeepClone() : null;
        var effective = MergeClone(file, overlay);
        return new ConfigNamespaceView(ns, SourceOf(ns), effective, IsEmpty(overlay) ? null : overlay);
    }

    public async Task<ConfigNamespaceView> SetAsync(string ns, JsonNode values, bool persist, CancellationToken ct = default)
    {
        var overlay = _runtime.GetOrAdd(ns, _ => new JsonObject());
        if (values is JsonObject incoming && overlay is JsonObject target)
            MergeInto(target, incoming);
        else
            overlay = _runtime[ns] = values.DeepClone();

        _updated[ns] = DateTimeOffset.UtcNow;

        if (persist)
            await WriteFileAsync(ns, overlay, ct).ConfigureAwait(false);

        var file = await ReadFileAsync(ns, ct).ConfigureAwait(false);
        return new ConfigNamespaceView(ns, persist ? ConfigSourceNames.File : ConfigSourceNames.Runtime,
            MergeClone(file, overlay), overlay.DeepClone());
    }

    public async Task<ConfigNamespaceView?> DeleteAsync(string ns, bool persist, CancellationToken ct = default)
    {
        if (!Exists(ns))
            return null;

        _runtime.TryRemove(ns, out _);
        _updated[ns] = DateTimeOffset.UtcNow;

        if (persist && File.Exists(PathOf(ns)))
            File.Delete(PathOf(ns));

        if (!Exists(ns))
            return new ConfigNamespaceView(ns, ConfigSourceNames.Default, null, null);

        var file = await ReadFileAsync(ns, ct).ConfigureAwait(false);
        return new ConfigNamespaceView(ns, SourceOf(ns), file?.DeepClone(), null);
    }

    private bool Exists(string ns) => _runtime.ContainsKey(ns) || File.Exists(PathOf(ns));

    private string SourceOf(string ns) =>
        _runtime.ContainsKey(ns) ? ConfigSourceNames.Runtime
        : File.Exists(PathOf(ns)) ? ConfigSourceNames.File
        : ConfigSourceNames.Default;

    private string PathOf(string ns) => Path.Combine(_dir, ns + ".jsonc");

    private async Task<JsonNode?> ReadFileAsync(string ns, CancellationToken ct)
    {
        var path = PathOf(ns);
        if (!File.Exists(path))
            return null;

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return JsonNode.Parse(text, nodeOptions: null, documentOptions: DocumentOptions);
        }
        catch (JsonException)
        {
            return null; // 非法 JSON：忽略文件层（视为无覆盖）
        }
    }

    private async Task WriteFileAsync(string ns, JsonNode values, CancellationToken ct)
    {
        Directory.CreateDirectory(_dir);
        var json = values.ToJsonString(WriteOptions);
        await File.WriteAllTextAsync(PathOf(ns), json, ct).ConfigureAwait(false);
    }

    private static JsonNode? MergeClone(JsonNode? file, JsonNode? overlay) =>
        MergeCloneInternal(file?.DeepClone(), overlay);

    private static JsonNode? MergeCloneInternal(JsonNode? baseNode, JsonNode? overlay)
    {
        if (overlay is null)
            return baseNode;
        if (baseNode is JsonObject b && overlay is JsonObject o)
        {
            foreach (var (key, value) in o)
            {
                if (value is null)
                    b.Remove(key);
                else
                    b[key] = MergeCloneInternal(b[key], value);
            }
            return b;
        }
        return overlay.DeepClone();
    }

    private static void MergeInto(JsonObject target, JsonObject incoming)
    {
        foreach (var (key, value) in incoming)
        {
            if (value is null)
                target.Remove(key);
            else if (value is JsonObject obj && target[key] is JsonObject existing)
                MergeInto(existing, obj);
            else
                target[key] = value.DeepClone();
        }
    }

    private static bool IsEmpty(JsonNode? node) => node is JsonObject obj && obj.Count == 0;
}
