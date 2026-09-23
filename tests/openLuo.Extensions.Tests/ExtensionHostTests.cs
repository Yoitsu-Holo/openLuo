using System.Text.Json;
using openLuo.Abstractions;
using openLuo.Capabilities.Core.Models;
using Xunit;

namespace openLuo.Extensions.Tests;

public sealed class ExtensionHostTests
{
    private sealed class TestExtension : IAgentExtension
    {
        public void Configure(ExtensionBuilder builder) => builder.AddCapability(new CapabilityDescriptor
        {
            CanonicalId = "echo",
            DisplayName = "Echo"
        });
    }

    [Fact]
    public void ManifestLoader_ParsesJsoncAndValidatesRequiredFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openluo-manifest-{Guid.NewGuid():N}.jsonc");
        try
        {
            File.WriteAllText(path, "// comment\n{\"id\":\"demo\",\"version\":\"1.2.0\",\"assembly\":\"demo.dll\",\"entryType\":\"Demo.Entry\",\"requires\":[{\"id\":\"base\",\"minVersion\":\"1.0.0\"}]}\n");
            var manifest = ExtensionManifestLoader.Load(path);

            Assert.NotNull(manifest);
            Assert.True(ExtensionManifestLoader.IsValid(manifest));
            Assert.Equal("demo", manifest.Id);
            Assert.Single(manifest.Requires);
            Assert.Equal("base", manifest.Requires[0].Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Host_SkipsDisabledDirectoriesAndNamespacesRegistrations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openluo-extensions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ignored.disable"));
        try
        {
            var assemblyPath = typeof(TestExtension).Assembly.Location;
            var manifest = new
            {
                id = "demo",
                version = "1.0.0",
                assembly = Path.GetFileName(assemblyPath),
                entryType = typeof(TestExtension).FullName
            };
            var extensionDir = Path.Combine(root, "demo");
            Directory.CreateDirectory(extensionDir);
            File.Copy(assemblyPath, Path.Combine(extensionDir, Path.GetFileName(assemblyPath)));
            File.WriteAllText(Path.Combine(extensionDir, "extension.jsonc"), JsonSerializer.Serialize(manifest));
            File.WriteAllText(Path.Combine(root, "ignored.disable", "extension.jsonc"), "{}");

            var host = new ExtensionHost(root, type => Activator.CreateInstance(type)!);
            var result = host.ScanAndLoad();

            var loaded = Assert.Single(result.Loaded);
            Assert.Equal("demo", loaded.Id);
            Assert.Equal("demo:echo", Assert.Single(loaded.Capabilities).CanonicalId);
            Assert.DoesNotContain(result.Diagnostics, d => d.ExtensionId == "ignored.disable");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Host_DisablesExtensionWhenDependencyDidNotLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), $"openluo-dependencies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var extensionDir = Path.Combine(root, "dependent");
            Directory.CreateDirectory(extensionDir);
            File.WriteAllText(Path.Combine(extensionDir, "extension.jsonc"), "{\"id\":\"dependent\",\"version\":\"1.0.0\",\"assembly\":\"missing.dll\",\"entryType\":\"Missing.Entry\",\"requires\":[{\"id\":\"missing\"}]}" );

            var host = new ExtensionHost(root, type => Activator.CreateInstance(type)!);
            var result = host.ScanAndLoad();

            Assert.Empty(result.Loaded);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Contains("missing dependency", diagnostic.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
