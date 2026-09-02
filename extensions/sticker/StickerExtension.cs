using System.Text.RegularExpressions;
using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Modules.Embedding.Core.Interfaces;

namespace OpenLuo.Extensions.Sticker;

/// <summary>
/// 表情发送扩展：本地表情图片库按"文件名即标注"（如 <c>无语-翻白眼-摆烂.png</c>）embedding 检索，
/// 模型给出自然语言描述 → 向量化 → 余弦相似度 top-p 加权随机选取 → 图片作为输出（QQ 渲染链路直发）。
/// 图片库目录：data/stickers/（publish 时随 openLuo/data 带入，生产可直接往该目录加文件）。
/// </summary>
public sealed class StickerExtension : IAgentExtension
{
    private readonly IEmbeddingClient _embedding;
    private readonly string _dataDir;

    public StickerExtension(IEmbeddingClient embedding, string? dataDir = null)
    {
        _embedding = embedding;
        _dataDir = dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
    }

    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(StickerDescriptors.Send, new StickerSendInvoker(_embedding, _dataDir));
    }
}

internal static class StickerDescriptors
{
    public static CapabilityDescriptor Send => new()
    {
        CanonicalId = "send_sticker", DisplayName = "Send sticker",
        Summary = "Send a sticker image expressing a mood, emotion, or meme that fits the conversation.",
        Usage = "Use when a sticker conveys the character's reaction better than words. Describe the desired expression naturally (e.g. 'speechless eye-roll', 'shocked cat', 'celebrating victory'). Send only when emotionally appropriate; do not overuse stickers.",
        Kind = CapabilityKind.Builtin, ProviderId = "sticker", SideEffect = SideEffectClass.ReadOnly,
        Completion = CompletionPolicy.Continue, ParallelSafe = false,
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                description = new { type = "string", description = "natural-language description of the mood, emotion, or meme to send" }
            }
        }
    };
}

/// <summary>文件名 → 检索标注解析。规则：去扩展名/尾部 (N)/常见前缀，分隔符 -/_ 转空格；无效名返回 null。</summary>
public static class StickerFileNames
{
    private static readonly string[] KnownPrefixes = ["微信图片_", "微信表情_", "IMG_", "img_", "image_"];
    private static readonly Regex TrailingNumber = new(@"[\s\-_]*[\(（]\d+[\)）]\s*$", RegexOptions.Compiled);
    private static readonly Regex PureNoise = new(@"^[\d\s\-_()（）.]+$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    public static bool IsSupportedImage(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>文件名 → 标注文本（embedding 输入）。清理后为空或纯数字/符号 → null（不进检索池）。</summary>
    public static string? ParseLabel(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var prefix in KnownPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }
        name = TrailingNumber.Replace(name, string.Empty);
        var label = name.Replace('-', ' ').Replace('_', ' ').Trim();
        if (label.Length == 0 || PureNoise.IsMatch(label)) return null;
        return label;
    }
}

public static class CosineSimilarity
{
    /// <summary>余弦相似度；维度不一致或零向量返回 0。</summary>
    public static float Compute(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA <= 0f || normB <= 0f) return 0f;
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}

public sealed class StickerHit
{
    public required string FilePath { get; init; }
    public required string Label { get; init; }
    public float Score { get; init; }
}

/// <summary>top-p 风格的加权随机选取：阈值过滤（绝对值兜底，候选少不退化为全选）→ top-k → 按 (score - threshold) 权重随机。</summary>
public static class StickerSelection
{
    public const float Threshold = 0.25f;
    public const int MaxCandidates = 5;

    public static StickerHit? Pick(IReadOnlyList<StickerHit> candidates, float threshold = Threshold, int maxCandidates = MaxCandidates, Random? random = null)
    {
        var rng = random ?? Random.Shared;
        var pool = candidates.Where(c => c.Score >= threshold)
            .OrderByDescending(c => c.Score)
            .Take(maxCandidates)
            .ToArray();
        if (pool.Length == 0) return null;
        var total = pool.Sum(c => Math.Max(0f, c.Score - threshold));
        if (total <= 0f) return pool[0];
        var roll = (float)rng.NextDouble() * total;
        foreach (var candidate in pool)
        {
            roll -= Math.Max(0f, candidate.Score - threshold);
            if (roll <= 0f) return candidate;
        }
        return pool[^1];
    }
}

internal sealed class StickerEntry
{
    public required string FilePath { get; init; }
    public required string Label { get; init; }
    public required float[] Vector { get; init; }
}

/// <summary>表情库索引：首次调用懒构建（扫描目录 + 逐个 embedding），内存缓存；构建失败可重试。</summary>
internal sealed class StickerIndex
{
    private readonly IEmbeddingClient _embedding;
    private readonly string _stickersDir;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private IReadOnlyList<StickerEntry>? _entries;

    public StickerIndex(IEmbeddingClient embedding, string dataDir)
    {
        _embedding = embedding;
        _stickersDir = Path.Combine(dataDir, "stickers");
    }

    public string StickersDirectory => _stickersDir;

    public async Task<IReadOnlyList<StickerEntry>?> EnsureBuiltAsync(CancellationToken ct)
    {
        if (_entries is not null) return _entries;
        await _buildLock.WaitAsync(ct);
        try
        {
            if (_entries is not null) return _entries;
            _entries = await BuildAsync(ct);
            return _entries;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private async Task<IReadOnlyList<StickerEntry>?> BuildAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_stickersDir)) return [];
        var files = Directory.EnumerateFiles(_stickersDir).Where(StickerFileNames.IsSupportedImage).ToList();
        if (files.Count == 0) return [];
        var entries = new List<StickerEntry>();
        foreach (var file in files)
        {
            var label = StickerFileNames.ParseLabel(Path.GetFileName(file));
            if (label is null) continue;
            var vector = await _embedding.EmbedAsync(label, ct);
            entries.Add(new StickerEntry { FilePath = file, Label = label, Vector = vector });
        }
        return entries;
    }
}

internal sealed class StickerSendInvoker : ICapabilityInvoker
{
    private readonly IEmbeddingClient _embedding;
    private readonly StickerIndex _index;

    public StickerSendInvoker(IEmbeddingClient embedding, string dataDir)
    {
        _embedding = embedding;
        _index = new StickerIndex(embedding, dataDir);
    }

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var description = call.Options.TryGetValue("description", out var descriptionOption) ? descriptionOption : string.Join(" ", call.Args);
        if (string.IsNullOrWhiteSpace(description))
            return Failed(call, "sticker description is empty; describe the mood or expression the character wants to send");
        if (!_embedding.Enabled)
            return Failed(call, "sticker capability requires embedding service (embedding.enabled=true)");

        IReadOnlyList<StickerEntry> entries;
        try
        {
            entries = await _index.EnsureBuiltAsync(ct) ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(call, $"sticker library unavailable: {ex.Message}");
        }
        if (entries.Count == 0)
            return Failed(call, "sticker library is empty or no file has a usable filename tag (name files like 无语-翻白眼.png)");

        float[] query;
        try
        {
            query = await _embedding.EmbedAsync(description, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(call, $"embedding query failed: {ex.Message}");
        }

        var hits = entries.Select(e => new StickerHit
        {
            FilePath = e.FilePath, Label = e.Label,
            Score = CosineSimilarity.Compute(query.AsSpan(), e.Vector.AsSpan())
        }).ToList();
        var hit = StickerSelection.Pick(hits);
        if (hit is null)
            return Failed(call, $"no sticker matches '{description}' (best similarity below threshold); reply with text instead");

        try
        {
            var bytes = File.ReadAllBytes(hit.FilePath);
            var payload = $"data:{MimeFor(hit.FilePath)};base64,{Convert.ToBase64String(bytes)}";
            return new CapabilityResult
            {
                InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok,
                Text = $"sent sticker tagged '{hit.Label}'",
                Outputs = [new OutputItem
                {
                    Id = Guid.NewGuid().ToString("N"), Kind = ReplyItemKind.Image, Payload = payload,
                    SourceCapability = call.CanonicalId, Fingerprint = $"sticker:{Path.GetFileName(hit.FilePath)}"
                }]
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(call, $"failed to read sticker file: {ex.Message}");
        }
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private static CapabilityResult Failed(CapabilityCall call, string error) => new()
    {
        InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Failed, Error = error
    };
}
