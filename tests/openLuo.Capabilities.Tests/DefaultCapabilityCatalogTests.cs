using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using Xunit;

namespace openLuo.Capabilities.Tests;

public class DefaultCapabilityCatalogTests
{
    private sealed class FakeSource(string providerId, params CapabilityDescriptor[] descriptors) : ICapabilitySource
    {
        public string ProviderId => providerId;
        public IReadOnlyList<CapabilityDescriptor> ListCapabilities() => descriptors;
    }

    private static CapabilityDescriptor D(string canonicalId, string providerId = "test") => new()
    {
        CanonicalId = canonicalId,
        Kind = CapabilityKind.Builtin,
        ProviderId = providerId
    };

    [Fact]
    public async Task LoadBase_CollectsFromAllSources()
    {
        var catalog = new DefaultCapabilityCatalog(
        [
            new FakeSource("a", D("a:x"), D("a:y")),
            new FakeSource("b", D("b:z"))
        ]);
        catalog.LoadBase();

        var snapshot = await catalog.BuildSnapshotAsync(new CatalogBuildContext());
        Assert.Equal(3, snapshot.ByCanonicalId.Count);
    }

    [Fact]
    public async Task BuildSnapshot_GeneratesBidirectionalMapping()
    {
        var catalog = new DefaultCapabilityCatalog([new FakeSource("a", D("a:x"))]);
        catalog.LoadBase();

        var snapshot = await catalog.BuildSnapshotAsync(new CatalogBuildContext());

        var descriptor = snapshot.ByCanonicalId["a:x"];
        Assert.False(string.IsNullOrWhiteSpace(descriptor.ModelToolName));
        Assert.Equal("a:x", snapshot.ModelNameToCanonicalId[descriptor.ModelToolName]);
        Assert.Equal(descriptor.ModelToolName, snapshot.CanonicalIdToModelName["a:x"]);
    }

    [Fact]
    public async Task BuildSnapshot_RespectsPermissions()
    {
        var catalog = new DefaultCapabilityCatalog([new FakeSource("a", D("a:x"), D("a:y"), D("a:z"))]);
        catalog.LoadBase();

        var snapshot = await catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            Permissions = new CapabilityPermissions
            {
                AllowedCanonicalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a:x" }
            }
        });

        Assert.Single(snapshot.ByCanonicalId);
        Assert.Contains("a:x", snapshot.ByCanonicalId.Keys);
    }

    [Fact]
    public async Task BuildSnapshot_WithoutExplicitLoadBase_AutoLoads()
    {
        var catalog = new DefaultCapabilityCatalog([new FakeSource("a", D("a:x"))]);

        var snapshot = await catalog.BuildSnapshotAsync(new CatalogBuildContext());

        Assert.True(snapshot.ByCanonicalId.ContainsKey("a:x"));
    }

    [Fact]
    public async Task BuildSnapshot_UnhealthyProvider_Excluded()
    {
        var catalog = new DefaultCapabilityCatalog(
        [
            new FakeSource("healthy", D("h:x", providerId: "healthy")),
            new FakeSource("down", D("d:y", providerId: "down"))
        ]);
        catalog.LoadBase();

        var snapshot = await catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            ProviderHealth = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["down"] = false
            }
        });

        Assert.Contains("h:x", snapshot.ByCanonicalId.Keys);
        Assert.DoesNotContain("d:y", snapshot.ByCanonicalId.Keys);
    }
}
