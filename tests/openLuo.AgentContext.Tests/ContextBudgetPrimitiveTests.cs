using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using Xunit;

namespace openLuo.AgentContext.Tests;

/// <summary>token 计数与区域保留策略（D44）。</summary>
public sealed class ContextBudgetPrimitiveTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("你好世界", 4)]          // CJK：1 字 ≈ 1 token（旧 Length/4 会算成 1）
    [InlineData("hello world", 3)]       // 11 字符 / 4 → 向上取整
    [InlineData("你好abc", 3)]           // 2 + ceil(3/4)
    [InlineData("abcd", 1)]
    [InlineData("，。「」", 4)]           // 全角标点同样按 1 token/字
    public void HeuristicCounter_EstimatesByWidth(string? text, int expected) =>
        Assert.Equal(expected, HeuristicTokenCounter.Instance.Count(text));

    [Fact]
    public void RegionPolicy_DeclaresEveryRegion()
    {
        // 新增 ContextRegion 成员时必须显式表态（否则这里红）——避免默认落进某个分支后被无声裁剪。
        var missing = Enum.GetValues<ContextRegion>().Except(ContextRegionPolicies.Declared).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void RegionPolicy_PinsBehaviourCriticalRegions()
    {
        Assert.Equal(ContextRetention.Pinned, ContextRegionPolicies.RetentionOf(ContextRegion.Identity));
        Assert.Equal(ContextRetention.Pinned, ContextRegionPolicies.RetentionOf(ContextRegion.RuntimeRules));
        Assert.Equal(ContextRetention.Pinned, ContextRegionPolicies.RetentionOf(ContextRegion.Capabilities));
        Assert.Equal(ContextRetention.Pinned, ContextRegionPolicies.RetentionOf(ContextRegion.CurrentUserInput));
        Assert.Equal(ContextRetention.Pinned, ContextRegionPolicies.RetentionOf(ContextRegion.ToolResults));
        // 记忆与场景可裁（预算紧张时让位给保底块）
        Assert.Equal(ContextRetention.Droppable, ContextRegionPolicies.RetentionOf(ContextRegion.LongTermMemory));
        Assert.Equal(ContextRetention.Droppable, ContextRegionPolicies.RetentionOf(ContextRegion.SceneState));
    }
}
