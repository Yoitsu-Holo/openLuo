using OpenLuo.Extensions.Music;
using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Interfaces.QQbot;
using openLuo.Server;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// Card 结构化载荷全链契约：产出扩展(music:share_song) → 内核 OutputItem(Card, 不透明 Payload)
/// → Hub 映射为 wire OutputDto → QQ 渲染(OneBot music 段)。内核不做平台判断，契约在两端。
/// </summary>
public sealed class MusicCardFlowTests
{
    private static OutputItem InvokeShareSong(Dictionary<string, string> options)
    {
        var builder = new ExtensionBuilder("music");
        new MusicExtension().Configure(builder);
        var (_, invoker) = Assert.Single(builder.InvokableCapabilities);

        var result = invoker.InvokeAsync(
            new CapabilityCall { InvocationId = "inv-1", CanonicalId = "music:share_song", Options = options },
            new CapabilityExecutionContext()).GetAwaiter().GetResult();

        Assert.True(result.Success, result.Error);
        return Assert.Single(result.Outputs);
    }

    [Fact]
    public void ShareSong_ProducesCard_AndQqRendersMusicSegment()
    {
        var card = InvokeShareSong(new Dictionary<string, string> { ["id"] = "1860163", ["title"] = "晴天" });
        Assert.Equal(ReplyItemKind.Card, card.Kind);
        Assert.Equal("music-card:163:1860163", card.Fingerprint);

        // 内核透传的 Payload → wire DTO → QQ 渲染层
        var part = QqRuntimeBridge.ToPart(WireMapper.ToDto(card));
        Assert.Equal("music", part.Kind);
        Assert.Equal("1860163", part.Value);
    }

    [Fact]
    public void NonNeteasePlatformCard_FallsBackToTextUrl()
    {
        var card = InvokeShareSong(new Dictionary<string, string>
        {
            ["id"] = "42", ["platform"] = "qq", ["url"] = "https://example.org/song/42"
        });
        Assert.Equal(ReplyItemKind.Card, card.Kind);

        var part = QqRuntimeBridge.ToPart(WireMapper.ToDto(card));
        Assert.Equal("text", part.Kind);
        Assert.Contains("https://example.org/song/42", part.Value);
    }

    [Fact]
    public void CardWithoutUrl_FallsBackToCompactJson()
    {
        var card = InvokeShareSong(new Dictionary<string, string> { ["id"] = "7", ["platform"] = "unknown" });
        var part = QqRuntimeBridge.ToPart(WireMapper.ToDto(card));
        Assert.Equal("text", part.Kind);
        Assert.StartsWith("[card]", part.Value);
    }
}
