using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using Xunit;

namespace openLuo.Cli.Tests;

public sealed class CliAdapterTests
{
    [Fact]
    public void Parser_SeparatesTextCommandArgsAndOptions()
    {
        var text = openLuo.Cli.CliInputParser.Parse("hello there");
        Assert.Equal(openLuo.Cli.CliInputKind.Text, text.Kind);
        Assert.Equal("hello there", text.Text);

        var command = openLuo.Cli.CliInputParser.Parse("/ask \"小艾\" question=今天好吗");
        Assert.Equal(openLuo.Cli.CliInputKind.Command, command.Kind);
        Assert.Equal("ask", command.Command);
        Assert.Equal(["小艾"], command.Args);
        Assert.Equal("今天好吗", command.Options["question"]);
    }

    [Fact]
    public void Renderer_HandlesTextAndMediaKinds()
    {
        Assert.Equal("hello", openLuo.Cli.CliRenderer.Render(new OutputItem { Kind = ReplyItemKind.Text, Payload = "hello" }));
        Assert.StartsWith("[image]", openLuo.Cli.CliRenderer.Render(new OutputItem { Kind = ReplyItemKind.Image, Payload = "data:image/png;base64,x" }));
    }
}
