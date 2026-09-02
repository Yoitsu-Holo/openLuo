using System.Text.Json;
using NSubstitute;
using OpenLuo.Extensions.Companion;
using OpenLuo.Extensions.Memory;
using OpenLuo.Extensions.Party;
using OpenLuo.Extensions.World;
using openLuo.Capabilities.Core.Models;
using openLuo.Abstractions;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Application.Runtime;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using Xunit;

namespace openLuo.Extensions.Tests;

public sealed class DomainExtensionContractTests
{
    [Fact]
    public void AllDomainManifests_AreValidAndUseExpectedEntries()
    {
        var root = FindRepositoryRoot();
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["memory"] = "OpenLuo.Extensions.Memory.MemoryExtension",
            ["companion"] = "OpenLuo.Extensions.Companion.CompanionExtension",
            ["world"] = "OpenLuo.Extensions.World.WorldExtension",
            ["party"] = "OpenLuo.Extensions.Party.PartyExtension",
        };

        foreach (var pair in expected)
        {
            var path = Path.Combine(root, "extensions", pair.Key, "extension.jsonc");
            var manifest = ExtensionManifestLoader.Load(path);
            Assert.NotNull(manifest);
            Assert.True(ExtensionManifestLoader.IsValid(manifest));
            Assert.Equal(pair.Key, manifest.Id);
            Assert.Equal(pair.Value, manifest.EntryType);
            Assert.True(File.Exists(Path.Combine(root, "extensions", pair.Key, manifest.Assembly.Replace(".dll", ".csproj", StringComparison.OrdinalIgnoreCase)))
                || pair.Key is "memory" or "companion" or "world" or "party");
        }
    }

    [Fact]
    public void MemoryExtension_RegistersSearchWriteAndBaseline()
    {
        var builder = new ExtensionBuilder("memory");
        new MemoryExtension(Substitute.For<IMemoryRecallService>(), Substitute.For<IMemoryWriteService>()).Configure(builder);
        Assert.Equal(["search", "write"], builder.Capabilities.Select(c => c.CanonicalId).ToArray());
        Assert.Equal("memory:baseline", Assert.Single(builder.ContextContributors).Instance is null
            ? "memory:baseline"
            : ((openLuo.AgentContext.Core.IContextContributor)builder.ContextContributors[0].Instance!).Id);
    }

    [Fact]
    public void DomainExtensions_RegisterCapabilities()
    {
        var companion = new ExtensionBuilder("companion");
        new CompanionExtension(Substitute.For<ILlmClient>()).Configure(companion);
        Assert.Contains("chat", companion.Capabilities.Select(c => c.CanonicalId));

        var world = new ExtensionBuilder("world");
        new WorldExtension(Substitute.For<IStateQueryService>(), Substitute.For<IStateMutationService>()).Configure(world);
        Assert.Contains("state.read", world.Capabilities.Select(c => c.CanonicalId));

        var party = new ExtensionBuilder("party");
        new PartyExtension(Substitute.For<IAgentRoster>(), Substitute.For<IAgentRuntimeHub>()).Configure(party);
        Assert.Contains("list_characters", party.Capabilities.Select(c => c.CanonicalId));

    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "openLuo.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("openLuo.slnx");
    }
}
