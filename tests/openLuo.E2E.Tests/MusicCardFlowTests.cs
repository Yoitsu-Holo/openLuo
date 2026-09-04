using OpenLuo.Extensions.Music;
using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Interfaces.QQbot;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// Card 结构化载荷全链契约：产出扩展(music:share_song) → 内核 OutputItem(Card, 不透明 Payload)
/// → QQ 平台渲染(OneBot music 段)。内核不在链上做任何平台判断，契约在两端。
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

        // 内核透传的 Payload 到达 QQ 渲染层
        var parts = QqRuntimeBridge.Render(new TurnResult
        {
            Success = true, Outputs = [card], TerminationReason = TerminationReason.FinalReply
        });
        var music = Assert.Single(parts.Where(p => p.Kind == "music"));
        Assert.Equal("1860163", music.Value);
    }

    [Fact]
    public void NonNeteasePlatformCard_FallsBackToTextUrl()
    {
        var card = InvokeShareSong(new Dictionary<string, string>
        {
            ["id"] = "42", ["platform"] = "qq", ["url"] = "https://example.org/song/42"
        });
        Assert.Equal(ReplyItemKind.Card, card.Kind);

        var parts = QqRuntimeBridge.Render(new TurnResult
        {
            Success = true, Outputs = [card], TerminationReason = TerminationReason.FinalReply
        });
        Assert.DoesNotContain(parts, p => p.Kind == "music");
        var text = Assert.Single(parts.Where(p => p.Kind == "text"));
        Assert.Contains("https://example.org/song/42", text.Value);
    }

    [Fact]
    public void CardWithoutUrl_FallsBackToCompactJson()
    {
        var card = InvokeShareSong(new Dictionary<string, string> { ["id"] = "7", ["platform"] = "unknown" });
        var parts = QqRuntimeBridge.Render(new TurnResult
        {
            Success = true, Outputs = [card], TerminationReason = TerminationReason.FinalReply
        });
        var text = Assert.Single(parts.Where(p => p.Kind == "text"));
        Assert.StartsWith("[card]", text.Value);
    }
}
