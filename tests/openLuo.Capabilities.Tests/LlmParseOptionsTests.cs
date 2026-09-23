using System.Reflection;
using Xunit;

namespace openLuo.Capabilities.Tests;

/// <summary>
/// 回归：LLM 工具调用参数解析（ParseOptions / ParseArgs）必须同时识别
/// 约定式包装 {"options":{...}} 与模型按 InputSchema properties 生成的顶层命名属性
/// （如 sticker 的 {"description":"..."}）。此前只解析 options 包装，导致声明了
/// 命名参数的能力（send_sticker 首例）参数永远丢失。
/// </summary>
public class LlmParseOptionsTests
{
    private static readonly Type DecisionModel =
        typeof(openLuo.Capabilities.Llm.LlmCapabilityDecisionModel);
    private static readonly MethodInfo ParseOptions = DecisionModel.GetMethod(
        "ParseOptions", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ParseArgs = DecisionModel.GetMethod(
        "ParseArgs", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Dictionary<string, string> Options(string json) =>
        (Dictionary<string, string>)ParseOptions.Invoke(null, [json])!;

    private static string[] Args(string json) =>
        (string[])ParseArgs.Invoke(null, [json])!;

    [Fact]
    public void ParseOptions_ReadsTopLevelNamedProperty()
    {
        // 模型按 InputSchema 的 properties 生成：sticker send_sticker 的实况形态
        var result = Options("""{"description":"震惊 摸不着头脑"}""");
        Assert.True(result.TryGetValue("description", out var value));
        Assert.Equal("震惊 摸不着头脑", value);
    }

    [Fact]
    public void ParseOptions_ReadsWrappedOptionsObject()
    {
        // 宿主代码构造 / 模型按 options 语义生成
        var result = Options("""{"options":{"description":"无语"}}""");
        Assert.True(result.TryGetValue("description", out var value));
        Assert.Equal("无语", value);
    }

    [Fact]
    public void ParseOptions_IgnoresArgsArrayKey()
    {
        // args 键进 Options 前被排除（由 ParseArgs 处理）
        var result = Options("""{"args":["x"],"description":"y"}""");
        Assert.DoesNotContain("args", result.Keys);
        Assert.True(result.TryGetValue("description", out var value));
        Assert.Equal("y", value);
    }

    [Fact]
    public void ParseOptions_PrefersWrappedOptions_WhenBothPresent()
    {
        // 两个来源同时存在时，options 包装优先（不覆盖）
        var result = Options("""{"options":{"description":"a"},"description":"b"}""");
        Assert.True(result.TryGetValue("description", out var value));
        Assert.Equal("a", value);
    }

    [Fact]
    public void ParseArgs_ReadsArgsArray()
    {
        var result = Args("""{"args":["震惊 摸不着头脑"]}""");
        Assert.Single(result);
        Assert.Equal("震惊 摸不着头脑", result[0]);
    }

    [Fact]
    public void ParseOptions_ReturnsEmpty_OnNullOrEmptyJson()
    {
        Assert.Empty(Options(""));
        Assert.Empty(Options("null"));
    }

    [Fact]
    public void ParseOptions_ReadsNumericTopLevelValue()
    {
        // 回归：share_song 首例——模型按 InputSchema（id: integer）传 {"id":27908590}，
        // 旧实现对数字 JsonValue 调 GetValue<string>() 抛异常并被整体吞掉 → id 静默丢失。
        var result = Options("""{"id":27908590}""");
        Assert.True(result.TryGetValue("id", out var value));
        Assert.Equal("27908590", value);
    }

    [Fact]
    public void ParseOptions_ReadsNumericAndBooleanInWrappedOptions()
    {
        var result = Options("""{"options":{"id":1860163,"vip":true}}""");
        Assert.Equal("1860163", result["id"]);
        Assert.Equal("true", result["vip"]);
    }

    [Fact]
    public void ParseOptions_MixedSources_WithNumber_BothKeptAndWrappedPreferred()
    {
        // options 包装(字符串)与顶层(数字)并存：顶层数字不丢失，options 优先不覆盖
        var result = Options("""{"options":{"limit":"5"},"id":27908590}""");
        Assert.Equal("5", result["limit"]);
        Assert.Equal("27908590", result["id"]);
    }

    [Fact]
    public void ParseOptions_ObjectValue_SkippedWithoutKillingOthers()
    {
        // 对象/数组值跳过（不进 options），其余命名属性仍解析
        var result = Options("""{"id":7,"nested":{"a":1},"list":[1,2]}""");
        Assert.Equal("7", result["id"]);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void ParseArgs_ReadsNumericArrayItems()
    {
        var result = Args("""{"args":[27908590]}""");
        Assert.Single(result);
        Assert.Equal("27908590", result[0]);
    }
}
