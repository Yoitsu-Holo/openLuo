using OpenLuo.Extensions.Music;
using openLuo.Abstractions;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using Xunit;

namespace openLuo.Extensions.Tests;

public sealed class MusicExtensionTests
{
    private static CapabilityCall Call(Dictionary<string, string> options) => new()
    {
        InvocationId = "inv-1",
        CanonicalId = "music:share_song",
        Options = options
    };

    [Fact]
    public void MusicExtension_RegistersShareSongCapability()
    {
        var builder = new ExtensionBuilder("music");
        new MusicExtension().Configure(builder);
        var capability = Assert.Single(builder.Capabilities);
        Assert.Equal("share_song", capability.CanonicalId);
        Assert.Equal("music", capability.ProviderId);
        Assert.Equal(CompletionPolicy.Continue, capability.Completion);
    }

    [Fact]
    public void ShareSong_ProducesStructuredCardOutput()
    {
        var invoker = new ShareSongInvoker();
        var result = invoker.InvokeAsync(
            Call(new Dictionary<string, string> { ["id"] = "1860163", ["title"] = "晴天" }),
            new CapabilityExecutionContext()).GetAwaiter().GetResult();

        Assert.True(result.Success);
        Assert.Equal(CapabilityStatus.Ok, result.Status);
        var item = Assert.Single(result.Outputs);
        Assert.Equal(ReplyItemKind.Card, item.Kind);
        Assert.Equal("music-card:163:1860163", item.Fingerprint);

        var payload = Assert.IsType<MusicShareCardPayload>(item.Payload);
        Assert.Equal("163", payload.Platform);
        Assert.Equal(1860163, payload.Id);
        Assert.Equal("晴天", payload.Title);
        Assert.Equal("https://music.163.com/#/song?id=1860163", payload.Url);
        Assert.Contains("do NOT call share_song again", result.Text);
    }

    [Fact]
    public void ShareSong_MissingId_Rejected()
    {
        var invoker = new ShareSongInvoker();
        var result = invoker.InvokeAsync(
            Call(new Dictionary<string, string>()),
            new CapabilityExecutionContext()).GetAwaiter().GetResult();

        Assert.False(result.Success);
        Assert.Equal(CapabilityStatus.Rejected, result.Status);
        Assert.Empty(result.Outputs);
    }

    [Fact]
    public void ShareSong_NonNumericId_Rejected()
    {
        var invoker = new ShareSongInvoker();
        var result = invoker.InvokeAsync(
            Call(new Dictionary<string, string> { ["id"] = "abc" }),
            new CapabilityExecutionContext()).GetAwaiter().GetResult();

        Assert.False(result.Success);
        Assert.Equal(CapabilityStatus.Rejected, result.Status);
    }

    [Fact]
    public void ShareSong_CustomPlatformAndUrl_ArePreserved()
    {
        var invoker = new ShareSongInvoker();
        var result = invoker.InvokeAsync(
            Call(new Dictionary<string, string>
            {
                ["id"] = "42", ["platform"] = "qq", ["url"] = "https://example.org/song/42"
            }),
            new CapabilityExecutionContext()).GetAwaiter().GetResult();

        var payload = Assert.IsType<MusicShareCardPayload>(Assert.Single(result.Outputs).Payload);
        Assert.Equal("qq", payload.Platform);
        Assert.Equal("https://example.org/song/42", payload.Url);
    }
}
