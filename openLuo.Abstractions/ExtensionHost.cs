using System.Reflection;
using System.Runtime.Loader;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Abstractions;

public sealed class ExtensionDiagnostic
{
    public string ExtensionId { get; init; } = string.Empty;
    public bool Loaded { get; init; }
    public string? Error { get; init; }
}

public sealed class ExtensionLoadResult
{
    public IReadOnlyList<ExtensionDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<LoadedExtension> Loaded { get; init; } = [];
}

public sealed class LoadedExtension
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string DataDir { get; init; } = string.Empty;
    public IReadOnlyList<CapabilityDescriptor> Capabilities { get; init; } = [];
    public IReadOnlyList<(CapabilityDescriptor Descriptor, ICapabilityInvoker Invoker)> InvokableCapabilities { get; init; } = [];
    public IReadOnlyList<ContextContributorRegistration> ContextContributors { get; init; } = [];
    public IReadOnlyList<WorkflowDefinition> Workflows { get; init; } = [];
    public IReadOnlyList<SkillDocument> Skills { get; init; } = [];
    public IReadOnlyList<IMessageTagRenderer> TagRenderers { get; init; } = [];
    public IReadOnlyList<StateMutationHandlerRegistration> StateHandlers { get; init; } = [];
}

/// <summary>
/// Scans and loads trusted in-process extensions once at startup.
/// Disabled directories, invalid manifests, dependency failures, and entry assembly failures are isolated per extension.
/// </summary>
public sealed class ExtensionHost
{
    private readonly string _extensionsRoot;
    private readonly Func<Type, object> _serviceFactory;
    private readonly List<LoadedExtension> _loaded = [];

    public ExtensionHost(string extensionsRoot, Func<Type, object> serviceFactory)
    {
        _extensionsRoot = extensionsRoot;
        _serviceFactory = serviceFactory;
    }

    public IReadOnlyList<LoadedExtension> Loaded => _loaded;

    public ExtensionLoadResult ScanAndLoad()
    {
        var diagnostics = new List<ExtensionDiagnostic>();
        _loaded.Clear();
        if (!Directory.Exists(_extensionsRoot))
            return new ExtensionLoadResult { Diagnostics = diagnostics, Loaded = _loaded };

        var manifests = new List<(string Dir, ExtensionManifest Manifest)>();
        foreach (var dir in Directory.EnumerateDirectories(_extensionsRoot).Where(dir => !IsDisabled(dir)))
        {
            var path = Path.Combine(dir, "extension.jsonc");
            if (!File.Exists(path))
            {
                diagnostics.Add(Failed(Path.GetFileName(dir), "missing extension.jsonc"));
                continue;
            }

            var manifest = ExtensionManifestLoader.Load(path);
            if (manifest is null || !ExtensionManifestLoader.IsValid(manifest))
            {
                diagnostics.Add(Failed(Path.GetFileName(dir), "invalid extension.jsonc"));
                continue;
            }

            manifests.Add((dir, manifest));
        }

        var byId = manifests.ToDictionary(x => x.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        var sorted = TopologicalSort(manifests.Select(x => x.Manifest).ToList(), byId, diagnostics);
        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in sorted)
        {
            var result = LoadOne(manifest, byId[manifest.Id].Dir, byId, loadedIds);
            diagnostics.Add(result.Diagnostic);
            if (result.Loaded is not null)
            {
                _loaded.Add(result.Loaded);
                loadedIds.Add(manifest.Id);
            }
        }

        return new ExtensionLoadResult { Diagnostics = diagnostics, Loaded = _loaded };
    }

    private (ExtensionDiagnostic Diagnostic, LoadedExtension? Loaded) LoadOne(
        ExtensionManifest manifest,
        string directory,
        IReadOnlyDictionary<string, (string Dir, ExtensionManifest Manifest)> byId,
        IReadOnlySet<string> loadedIds)
    {
        foreach (var dependency in manifest.Requires)
        {
            if (!byId.TryGetValue(dependency.Id, out var dependencyEntry))
                return (Failed(manifest.Id, $"missing dependency: {dependency.Id}"), null);
            if (!loadedIds.Contains(dependency.Id))
                return (Failed(manifest.Id, $"dependency failed or disabled: {dependency.Id}"), null);
            if (!VersionAtLeast(dependencyEntry.Manifest.Version, dependency.MinVersion))
                return (Failed(manifest.Id, $"dependency version too low: {dependency.Id} ({dependencyEntry.Manifest.Version} < {dependency.MinVersion})"), null);
        }

        try
        {
            var assemblyPath = Path.Combine(directory, manifest.Assembly);
            if (!File.Exists(assemblyPath))
                return (Failed(manifest.Id, $"assembly not found: {manifest.Assembly}"), null);

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var entryType = assembly.GetType(manifest.EntryType, throwOnError: false);
            if (entryType is null || !typeof(IAgentExtension).IsAssignableFrom(entryType))
                return (Failed(manifest.Id, $"entry type not found or not IAgentExtension: {manifest.EntryType}"), null);

            var created = _serviceFactory(entryType);
            var extension = created as IAgentExtension ?? (IAgentExtension)Activator.CreateInstance(entryType)!;
            var builder = new ExtensionBuilder(manifest.Id);
            extension.Configure(builder);

            return (new ExtensionDiagnostic { ExtensionId = manifest.Id, Loaded = true }, Namespace(manifest, builder, Path.Combine(directory, manifest.DataDir)));
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException is null ? string.Empty : $" inner={ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            var stack = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
            return (Failed(manifest.Id, $"load failed: {ex.GetType().Name}: {ex.Message}{inner} @ {stack}"), null);
        }
    }

    private static LoadedExtension Namespace(ExtensionManifest manifest, ExtensionBuilder builder, string dataDir)
    {
        var prefix = manifest.Id;
        var capabilities = builder.Capabilities.Select(d => NamespaceCapability(d, prefix)).ToList();
        var invokable = builder.InvokableCapabilities.Select(pair => (NamespaceCapability(pair.Descriptor, prefix), pair.Invoker)).ToList();
        var workflows = builder.Workflows.Select(w => new WorkflowDefinition
        {
            Id = NamespaceId(w.Id, prefix), Description = w.Description, StartNodeId = w.StartNodeId,
            MaxSteps = w.MaxSteps, Nodes = w.Nodes, Edges = w.Edges
        }).ToList();
        var skills = builder.Skills.Select(s => new SkillDocument
        {
            Id = NamespaceId(s.Id, prefix), Title = s.Title, Summary = s.Summary, WhenToUse = s.WhenToUse,
            FullContent = s.FullContent, RelatedTools = s.RelatedTools, RelatedWorkflows = s.RelatedWorkflows, Constraints = s.Constraints
        }).ToList();

        return new LoadedExtension
        {
            Id = manifest.Id, Version = manifest.Version, DataDir = dataDir,
            Capabilities = capabilities, InvokableCapabilities = invokable,
            ContextContributors = builder.ContextContributors, Workflows = workflows, Skills = skills,
            TagRenderers = builder.TagRenderers, StateHandlers = builder.StateHandlers
        };
    }

    private static CapabilityDescriptor NamespaceCapability(CapabilityDescriptor descriptor, string prefix)
    {
        if (descriptor.CanonicalId.StartsWith("core:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"extension '{prefix}' attempted to register reserved core capability '{descriptor.CanonicalId}'");
        return descriptor with { CanonicalId = NamespaceId(descriptor.CanonicalId, prefix) };
    }

    private static string NamespaceId(string localId, string prefix) =>
        localId.Contains(':', StringComparison.OrdinalIgnoreCase) ? localId : $"{prefix}:{localId}";

    private static ExtensionDiagnostic Failed(string id, string error) => new() { ExtensionId = id, Loaded = false, Error = error };
    private static bool IsDisabled(string directory) => Path.GetFileName(directory).EndsWith(".disable", StringComparison.OrdinalIgnoreCase);

    private static bool VersionAtLeast(string actual, string minimum) => ParseVersion(actual).CompareTo(ParseVersion(minimum)) >= 0;
    private static Version ParseVersion(string value) => Version.TryParse(value, out var version) ? version : new Version(0, 0, 0);

    private static List<ExtensionManifest> TopologicalSort(
        IReadOnlyList<ExtensionManifest> manifests,
        IReadOnlyDictionary<string, (string Dir, ExtensionManifest Manifest)> byId,
        List<ExtensionDiagnostic> diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ExtensionManifest>();
        foreach (var manifest in manifests)
            Visit(manifest, byId, visited, active, result, diagnostics);
        return result;
    }

    private static bool Visit(
        ExtensionManifest manifest,
        IReadOnlyDictionary<string, (string Dir, ExtensionManifest Manifest)> byId,
        HashSet<string> visited,
        HashSet<string> active,
        List<ExtensionManifest> result,
        List<ExtensionDiagnostic> diagnostics)
    {
        if (visited.Contains(manifest.Id)) return true;
        if (!active.Add(manifest.Id))
        {
            diagnostics.Add(Failed(manifest.Id, $"circular dependency detected at: {manifest.Id}"));
            return false;
        }
        foreach (var dependency in manifest.Requires)
            if (byId.TryGetValue(dependency.Id, out var entry) && !Visit(entry.Manifest, byId, visited, active, result, diagnostics))
                return false;
        active.Remove(manifest.Id);
        visited.Add(manifest.Id);
        result.Add(manifest);
        return true;
    }
}
