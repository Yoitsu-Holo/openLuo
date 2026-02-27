using openLuo.Modules.Gameplay.Application.Services;
using openLuo.Core.Interfaces;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using NSubstitute;
using Xunit;

namespace openLuo.Tests.Application;

public class StatusAggregatorTests
{
    private readonly IResourceStatusProjectionService _projection = Substitute.For<IResourceStatusProjectionService>();
    private readonly StatusAggregator _aggregator;

    public StatusAggregatorTests()
    {
        _aggregator = new StatusAggregator(_projection);
    }

    [Fact]
    public async Task GetStatusAsync_ShouldAggregateStatesAndPluginItems()
    {
        var defs = new List<StateDef>
        {
            new()
            {
                Namespace = "char_status",
                Key = "affection",
                OwnerKind = StateOwnerKind.Character,
                ValueType = StateValueType.Number,
                MaxValue = "1000",
                StatusGroup = "intimacy",
                MetadataJson = """{"name":"好感度"}"""
            },
            new()
            {
                Namespace = "game_resource",
                Key = "gold",
                OwnerKind = StateOwnerKind.Game,
                ValueType = StateValueType.Number,
                StatusGroup = "currency",
                MetadataJson = """{"name":"金币"}"""
            }
        };

        _projection.BuildStatusSnapshotAsync(Arg.Any<ResourceStatusQuery>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new ResourceStatusSnapshot { GameId = "g1", CharacterId = "c1", Items = [
                new ResourceStatusItemView { Id = "affection", Key = "affection", Value = "420", Group = "intimacy" },
                new ResourceStatusItemView { Id = "gold", Key = "gold", Value = "500", Group = "currency" }
            ] });

        var result = await _aggregator.GetStatusAsync("g1", "c1", "builtin-yimei");

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.Id == "affection" && i.Value == "420");
        Assert.Contains(result.Items, i => i.Id == "gold" && i.Value == "500");
    }

    [Fact]
    public async Task GetStatusAsync_ShouldOrderByGroupAndOrder()
    {
        var defs = new List<StateDef>
        {
            new()
            {
                Namespace = "char_status",
                Key = "stress",
                OwnerKind = StateOwnerKind.Character,
                ValueType = StateValueType.Number,
                StatusGroup = "physical",
                StatusOrder = 200
            },
            new()
            {
                Namespace = "char_status",
                Key = "affection",
                OwnerKind = StateOwnerKind.Character,
                ValueType = StateValueType.Number,
                StatusGroup = "intimacy",
                StatusOrder = 100
            }
        };

        _projection.BuildStatusSnapshotAsync(Arg.Any<ResourceStatusQuery>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new ResourceStatusSnapshot { GameId = "g1", CharacterId = "c1", Items = [
                new ResourceStatusItemView { Id = "affection", Key = "affection", Value = "100", Group = "intimacy", Order = 100 },
                new ResourceStatusItemView { Id = "stress", Key = "stress", Value = "100", Group = "physical", Order = 200 }
            ] });

        var result = await _aggregator.GetStatusAsync("g1", "c1", "bg1");

        Assert.Equal("intimacy", result.Items[0].Group);
        Assert.Equal("physical", result.Items[1].Group);
    }

    [Fact]
    public async Task GetStatusAsync_ShouldParsePluginStatusItems()
    {
        _projection.BuildStatusSnapshotAsync(Arg.Any<ResourceStatusQuery>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new ResourceStatusSnapshot { GameId = "g1", CharacterId = "c1", Items = [
                new ResourceStatusItemView { Id = "custom", Label = "自定义", Type = "text", Value = "test", Group = "custom", Order = 50, Text = "插件项", FromPluginHook = true }
            ] });

        var result = await _aggregator.GetStatusAsync("g1", "c1", "bg1");

        Assert.Single(result.Items);
        Assert.Equal("custom", result.Items[0].Id);
        Assert.Equal("插件项", result.Items[0].Text);
    }

    [Fact]
    public async Task GetStatusAsync_ShouldPassStateSnapshotWithoutLegacyResourceSnapshot()
    {
        _projection.BuildStatusSnapshotAsync(Arg.Any<ResourceStatusQuery>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new ResourceStatusSnapshot { GameId = "g1", CharacterId = "c1", Items = [
                new ResourceStatusItemView { Id = "affection", Key = "affection", Value = "420" }
            ] });

        var result = await _aggregator.GetStatusAsync("g1", "c1", "bg1");

        Assert.Single(result.Items);
        Assert.Equal("420", result.Items[0].Value);
    }

    [Fact]
    public async Task GetStatusAsync_ShouldFallbackToDefaultRenderingWhenPluginMissing()
    {
        var defs = new List<StateDef>
        {
            new()
            {
                Namespace = "char_status",
                Key = "health",
                OwnerKind = StateOwnerKind.Character,
                ValueType = StateValueType.Number,
                MaxValue = "100",
                DisplayFormat = "{value}/{max}",
                StatusGroup = "physical",
                MetadataJson = """{"name":"健康"}"""
            }
        };

        _projection.BuildStatusSnapshotAsync(Arg.Any<ResourceStatusQuery>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(new ResourceStatusSnapshot { GameId = "g1", CharacterId = "c1", Items = [
                new ResourceStatusItemView { Id = "health", Key = "health", Label = "健康", Type = "bar", Value = "80", Max = "100", Group = "physical" }
            ] });

        var result = await _aggregator.GetStatusAsync("g1", "c1", "bg1");

        Assert.Single(result.Items);
        Assert.Equal("health", result.Items[0].Id);
        Assert.Equal("80", result.Items[0].Value);
        Assert.Equal("bar", result.Items[0].Type);
    }
}
