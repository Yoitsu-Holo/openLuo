using NSubstitute;
using OpenLuo.Extensions.Sticker;
using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Modules.Embedding.Core.Interfaces;
using Xunit;

namespace openLuo.Extensions.Tests;

public sealed class StickerExtensionTests
{
    // ── 注册契约 ─────────────────────────────────────────────────────────

    [Fact]
    public void StickerExtension_RegistersSendCapability()
    {
        var builder = new ExtensionBuilder("sticker");
        new StickerExtension(Substitute.For<IEmbeddingClient>()).Configure(builder);
        var capability = Assert.Single(builder.Capabilities);
        Assert.Equal("send_sticker", capability.CanonicalId);
        Assert.Equal("sticker", capability.ProviderId);
        Assert.Equal(Capabilities.Core.Models.CapabilityKind.Builtin, capability.Kind);
    }

    // ── 文件名解析 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("无语-翻白眼-摆烂.png", "无语 翻白眼 摆烂")]
    [InlineData("震惊.png", "震惊")]
    [InlineData("猫猫-震惊-竖中指.gif", "猫猫 震惊 竖中指")]
    [InlineData("meme-2023.jpg", "meme 2023")]
    [InlineData("  空格-标签  .png", "空格 标签")]
    [InlineData("IMG_1234.jpg", null)]
    [InlineData("微信图片_20240901_123456.png", null)]
    [InlineData("(1).png", null)]
    [InlineData("12345.png", null)]
    [InlineData("meme(1).png", "meme")]
    [InlineData("meme（2）.png", "meme")]
    public void ParseLabel_ExtractsTagsFromFileName(string fileName, string? expected)
    {
        Assert.Equal(expected, StickerFileNames.ParseLabel(fileName));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    public void IsSupportedImage_RecognizesImageExtensions(string fileName, bool expected)
    {
        Assert.Equal(expected, StickerFileNames.IsSupportedImage(fileName));
    }

    // ── 余弦相似度 ────────────────────────────────────────────────────────

    [Fact]
    public void CosineSimilarity_IdenticalVectors_IsOne()
    {
        Assert.Equal(1f, CosineSimilarity.Compute([1f, 2f, 3f], [1f, 2f, 3f]), precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_IsZero()
    {
        Assert.Equal(0f, CosineSimilarity.Compute([1f, 0f], [0f, 1f]), precision: 4);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_IsMinusOne()
    {
        Assert.Equal(-1f, CosineSimilarity.Compute([1f, 0f], [-1f, 0f]), precision: 4);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_IsZero()
    {
        Assert.Equal(0f, CosineSimilarity.Compute([0f, 0f], [1f, 1f]), precision: 4);
        Assert.Equal(0f, CosineSimilarity.Compute([1f], [1f, 2f]), precision: 4);
    }

    // ── top-p 加权选取 ────────────────────────────────────────────────────

    [Fact]
    public void Selection_AllBelowThreshold_ReturnsNull()
    {
        var hits = Hits(("a", 0.1f), ("b", 0.2f));
        Assert.Null(StickerSelection.Pick(hits));
    }

    [Fact]
    public void Selection_EmptyCandidates_ReturnsNull()
    {
        Assert.Null(StickerSelection.Pick([]));
    }

    [Fact]
    public void Selection_OnlyPicksFromAboveThresholdPool()
    {
        // 0.9/0.8 在池内；0.2 低于阈值。多次随机采样必须只命中前两者。
        var hits = Hits(("best", 0.9f), ("good", 0.8f), ("poor", 0.2f));
        var picked = new HashSet<string>();
        var rng = new Random(42);
        for (var i = 0; i < 200; i++)
            picked.Add(StickerSelection.Pick(hits, random: rng)!.Label);
        Assert.Equal(["best", "good"], picked.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Selection_HigherScore_IsPickedMoreOften()
    {
        var hits = Hits(("best", 0.9f), ("good", 0.3f));
        var rng = new Random(7);
        var best = 0;
        const int trials = 500;
        for (var i = 0; i < trials; i++)
            if (StickerSelection.Pick(hits, random: rng)!.Label == "best")
                best++;
        Assert.True(best > trials * 0.8, $"expected best to dominate, got {best}/{trials}");
    }

    [Fact]
    public void Selection_RespectsTopKCap()
    {
        var hits = Hits(("a", 0.9f), ("b", 0.8f), ("c", 0.7f), ("d", 0.6f));
        var rng = new Random(1);
        var picked = new HashSet<string>();
        for (var i = 0; i < 200; i++)
            picked.Add(StickerSelection.Pick(hits, maxCandidates: 2, random: rng)!.Label);
        Assert.Equal(["a", "b"], picked.OrderBy(x => x).ToArray());
    }

    // ── Invoker 端到端（目录 → 索引 → 查询 → 选取 → 图片输出）───────────

    [Fact]
    public async Task SendInvoker_EndToEnd_ProducesImageOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sticker-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "stickers"));
        try
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
            File.WriteAllBytes(Path.Combine(dir, "stickers", "无语-翻白眼.png"), bytes);

            var embedding = Substitute.For<IEmbeddingClient>();
            embedding.Enabled.Returns(true);
            // 标签 "无语 翻白眼" 的向量与查询 "想要一个无语的表情" 高度接近（其余标签正交）
            embedding.EmbedAsync("无语 翻白眼", Arg.Any<CancellationToken>()).Returns([1f, 0f, 0f]);
            embedding.EmbedAsync("想要一个无语的表情", Arg.Any<CancellationToken>()).Returns([0.9f, 0.1f, 0f]);

            var invoker = new StickerSendInvoker(embedding, dir);
            var result = await invoker.InvokeAsync(new CapabilityCall
            {
                InvocationId = "inv-1", CanonicalId = "send_sticker",
                Options = new Dictionary<string, string> { ["description"] = "想要一个无语的表情" }
            }, new CapabilityExecutionContext());

            Assert.True(result.Success);
            Assert.Equal(CapabilityStatus.Ok, result.Status);
            var output = Assert.Single(result.Outputs);
            Assert.Equal(ReplyItemKind.Image, output.Kind);
            var payload = Assert.IsType<string>(output.Payload);
            Assert.StartsWith("data:image/png;base64,", payload);
            Assert.Equal(Convert.ToBase64String(bytes), payload["data:image/png;base64,".Length..]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SendInvoker_EmptyDescription_Fails()
    {
        var embedding = Substitute.For<IEmbeddingClient>();
        embedding.Enabled.Returns(true);
        var invoker = new StickerSendInvoker(embedding, Path.GetTempPath());
        var result = await invoker.InvokeAsync(new CapabilityCall
        {
            InvocationId = "inv-2", CanonicalId = "send_sticker",
            Options = new Dictionary<string, string> { ["description"] = "  " }
        }, new CapabilityExecutionContext());
        Assert.False(result.Success);
        Assert.Contains("description is empty", result.Error);
    }

    [Fact]
    public async Task SendInvoker_EmbeddingDisabled_Fails()
    {
        var embedding = Substitute.For<IEmbeddingClient>();
        embedding.Enabled.Returns(false);
        var invoker = new StickerSendInvoker(embedding, Path.GetTempPath());
        var result = await invoker.InvokeAsync(new CapabilityCall
        {
            InvocationId = "inv-3", CanonicalId = "send_sticker",
            Options = new Dictionary<string, string> { ["description"] = "无语" }
        }, new CapabilityExecutionContext());
        Assert.False(result.Success);
        Assert.Contains("embedding service", result.Error);
    }

    private static List<StickerHit> Hits(params (string Label, float Score)[] items) =>
        items.Select(i => new StickerHit { FilePath = $"{i.Label}.png", Label = i.Label, Score = i.Score }).ToList();
}
