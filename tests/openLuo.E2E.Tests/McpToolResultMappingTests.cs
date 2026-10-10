using ModelContextProtocol.Protocol;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Mcp;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// MCP 工具结果 → 能力状态的契约：<c>isError=true</c> 必须映射为 <c>Failed</c>（错误原文仍回填给模型），
/// 否则 Hub 会把失败的工具调用记成 <c>ok</c>（真实错误只剩 trace preview 可查，误导排查）。
/// 服务端一侧的对应约定：Python MCP 工具失败要 <c>raise ToolError(...)</c>，不要吞成普通文本。
/// </summary>
public sealed class McpToolResultMappingTests
{
    private static CallToolResult Result(bool isError, params string[] texts) => new()
    {
        IsError = isError,
        Content = [.. texts.Select(t => new TextContentBlock { Text = t })],
    };

    [Fact]
    public void IsError_result_is_mapped_to_failed_and_keeps_error_text_for_the_model()
    {
        var mapped = McpCapabilityInvoker.MapToolResult(
            "inv-1", "qwen-tts", "qwen_tts_speak", "mcp:qwen-tts:qwen_tts_speak",
            Result(true, "Error executing tool qwen_tts_speak: 语音合成失败: Connection reset by peer"));

        Assert.False(mapped.Success);
        Assert.Equal(CapabilityStatus.Failed, mapped.Status);
        Assert.Equal("inv-1", mapped.InvocationId);
        Assert.Contains("语音合成失败", mapped.Error);
        // 错误原文同样进 Text：模型仍能看到失败原因并自然回应，而不是拿到空结果
        Assert.Contains("语音合成失败", mapped.Text);
        Assert.Empty(mapped.Outputs);
    }

    [Fact]
    public void Successful_result_is_ok_and_has_no_error()
    {
        var mapped = McpCapabilityInvoker.MapToolResult(
            "inv-2", "qwen-tts", "qwen_tts_speak", "mcp:qwen-tts:qwen_tts_speak",
            Result(false, "本次合成完成(共 4 字)：你好呀"));

        Assert.True(mapped.Success);
        Assert.Equal(CapabilityStatus.Ok, mapped.Status);
        Assert.Null(mapped.Error);
        Assert.Contains("你好呀", mapped.Text);
    }

    [Fact]
    public void Empty_error_content_still_yields_a_named_failure()
    {
        var mapped = McpCapabilityInvoker.MapToolResult(
            "inv-3", "media", "search_image", "mcp:media:search_image",
            Result(true, "   "));

        Assert.False(mapped.Success);
        Assert.Equal("mcp tool failed: search_image", mapped.Error);
    }

    [Fact]
    public void Inline_data_url_becomes_output_item()
    {
        var mapped = McpCapabilityInvoker.MapToolResult(
            "inv-4", "qwen-tts", "qwen_tts_speak", "mcp:qwen-tts:qwen_tts_speak",
            Result(false, "本次朗读完成", "data:audio/wav;base64,UklGRg=="));

        Assert.True(mapped.Success);
        var output = Assert.Single(mapped.Outputs);
        Assert.Equal(ReplyItemKind.Audio, output.Kind);
        Assert.Equal("data:audio/wav;base64,UklGRg==", output.Payload);
        Assert.Contains("本次朗读完成", mapped.Text);
    }
}
