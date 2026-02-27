using openLuo.Core.Interfaces;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.Gameplay.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace openLuo.Tests.Application;

/// <summary>
/// Tests for resource evaluation coordination logic.
/// Tests validation, clamping, and LLM integration patterns.
/// </summary>
public class StateEvaluationCoordinatorTests
{
    [Fact]
    public void StateDef_ShouldValidateMutableByLlm()
    {
        var mutableDef = new StateDef { Namespace = "char_status", Key = "affection", MutableByLlm = true };
        var immutableDef = new StateDef { Namespace = "char_status", Key = "relationship_stage", MutableByLlm = false };

        Assert.True(mutableDef.MutableByLlm);
        Assert.False(immutableDef.MutableByLlm);
    }

    [Fact]
    public void StateDef_Metadata_ShouldSupportMaxDeltaPerTurn()
    {
        var def = new StateDef
        {
            Namespace = "char_status",
            Key = "affection",
            MinValue = "0",
            MaxValue = "1000",
            MetadataJson = """{"maxDeltaPerTurn":20}"""
        };

        Assert.Equal("""{"maxDeltaPerTurn":20}""", def.MetadataJson);
    }

    [Fact]
    public void StateDef_ShouldSupportDerivedFlag()
    {
        var primary = new StateDef { Namespace = "char_status", Key = "affection", Derived = false, MutableByLlm = true };
        var derived = new StateDef { Namespace = "char_status", Key = "relationship_stage", Derived = true, MutableByLlm = false };

        Assert.False(primary.Derived);
        Assert.True(derived.Derived);
    }

    [Fact]
    public async Task EvaluateStatesAsync_ShouldPassStateSnapshotToPromptHook()
    {
        var llmClient = Substitute.For<ILlmClient>();
        var pluginHost = Substitute.For<IPluginHost>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var logger = Substitute.For<ILogger<StateEvaluationCoordinator>>();
        var stateMutationService = Substitute.For<IStateMutationService>();
        var contentRegistry = new ContentRegistry(new Dictionary<ContentKind, IReadOnlyDictionary<string, RegistryEntry>>());
        OnPromptContextInput? capturedInput = null;

        var def = new StateDef
        {
            Namespace = "char_status",
            Key = "affection",
            OwnerKind = StateOwnerKind.Character,
            ValueType = StateValueType.Number,
            MutableByLlm = true
        };

        // replaced by evalProjection mock
        pluginHost.CallPromptContextHookAsync(Arg.Any<OnPromptContextInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedInput = call.Arg<OnPromptContextInput>();
                return [];
            });
        serviceProvider.GetService(typeof(IPluginHost)).Returns(pluginHost);
        llmClient.CompleteAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<LlmOptions?>(), Arg.Any<CancellationToken>())
            .Returns("""{"resourceChanges":[],"reason":"ok"}""");

        var evalProjection = Substitute.For<IResourceEvaluationProjectionService>();
        evalProjection.BuildEvaluationSnapshotAsync(Arg.Any<ResourceEvaluationQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ResourceEvaluationSnapshot { GameId = "g1", CharacterId = "c1", Items = [], StateSnapshot = new Dictionary<string, Dictionary<string, string>> { ["charStatus"] = new Dictionary<string, string> { ["affection"] = "420" } } });
        var coordinator = new StateEvaluationCoordinator(llmClient, serviceProvider, logger, stateMutationService, contentRegistry, evalProjection);

        var result = await coordinator.EvaluateStatesAsync(
            "g1",
            "c1",
            "bg1",
            "一起聊天",
            ["happy"],
            "你好",
            "chat");

        Assert.Empty(result.StateChanges);
        Assert.NotNull(capturedInput);
        Assert.NotNull(capturedInput!.StateSnapshot);
        Assert.True(capturedInput.StateSnapshot!.TryGetValue("charStatus", out var ns));
        Assert.Equal("420", ns["affection"]);
    }
}
