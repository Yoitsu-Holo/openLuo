using System.Text.Json.Serialization;
using openLuo.Modules.Executor.Infrastructure;

namespace openLuo.Executor.Tests;

public sealed class StructuredOutputParserTests
{
    [Fact]
    public void Parse_ReadsRawJson()
    {
        var parser = new StructuredOutputParser();

        var result = parser.Parse<DemoOutput>("""{"decision":"ok","score":3}""");

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("ok", result.Value!.Decision);
        Assert.Equal(3, result.Value.Score);
    }

    [Fact]
    public void Parse_ReadsMarkdownJsonFence()
    {
        var parser = new StructuredOutputParser();

        var result = parser.Parse<DemoOutput>(
            """
            ```json
            {
              "decision": "continue",
              "score": 7
            }
            ```
            """);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("continue", result.Value!.Decision);
        Assert.Equal(7, result.Value.Score);
    }

    [Fact]
    public void Parse_ExtractsJsonObjectFromMixedText()
    {
        var parser = new StructuredOutputParser();

        var result = parser.Parse<DemoOutput>(
            """
            下面是结果：
            {"decision":"stop","score":1}
            以上。
            """);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal("stop", result.Value!.Decision);
        Assert.Equal(1, result.Value.Score);
    }

    [Fact]
    public void Parse_ReturnsFailureForInvalidJson()
    {
        var parser = new StructuredOutputParser();

        var result = parser.Parse<DemoOutput>("```json\n{\"decision\":\n```");

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON", result.Error);
    }

    private sealed class DemoOutput
    {
        [JsonPropertyName("decision")]
        public string Decision { get; init; } = string.Empty;

        [JsonPropertyName("score")]
        public int Score { get; init; }
    }
}
