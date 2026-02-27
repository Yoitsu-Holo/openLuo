using openLuo.Modules.Content.Application.Validation;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Registry;

public sealed class ContentRegistryBuilder
{
    private readonly IContentValidator _validator;
    private readonly Dictionary<ContentKind, Dictionary<string, RegistryEntry>> _entries = [];
    private readonly List<PackManifest> _manifests = [];

    public ContentRegistryBuilder(IContentValidator validator)
    {
        _validator = validator;
    }

    public IReadOnlyList<PackManifest> Manifests => _manifests;

    public ContentRegistryBuilder AddPack(PackManifest manifest)
    {
        _manifests.Add(manifest);
        AddDefinition(manifest, manifest.Id, sourcePath: null, isLegacy: false);
        return this;
    }

    public ContentRegistryBuilder AddDefinition(ContentDefinitionBase definition, string sourcePack, string? sourcePath = null, bool isLegacy = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePack);

        definition.SourcePack = sourcePack;
        AddDefinition(definition, sourcePack, sourcePath, isLegacy, overrideExisting: true);
        return this;
    }

    public ContentRegistry Build()
    {
        foreach (var manifest in _manifests)
        {
            var definitions = _entries.Values
                .SelectMany(x => x.Values)
                .Where(x => x.SourcePack.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                            x.Kind is not ContentKind.PackManifest)
                .Select(x => x.Definition)
                .ToArray();

            var validation = _validator.Validate(manifest, definitions);
            if (!validation.IsValid)
            {
                var errors = validation.Issues
                    .Where(issue => issue.Severity == ContentValidationSeverity.Error)
                    .Select(issue => $"{issue.Kind}:{issue.Id} {issue.Message}");
                throw new InvalidOperationException($"Content validation failed for pack '{manifest.Id}': {string.Join(" | ", errors)}");
            }
        }

        var snapshot = _entries.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, RegistryEntry>)pair.Value.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase));

        return new ContentRegistry(snapshot);
    }

    private void AddDefinition(ContentDefinitionBase definition, string sourcePack, string? sourcePath, bool isLegacy, bool overrideExisting)
    {
        var kindEntries = GetOrCreateKindBucket(definition.Kind);
        var normalizedId = NormalizeId(definition.Id);

        if (!overrideExisting && kindEntries.ContainsKey(normalizedId))
            return;

        kindEntries[normalizedId] = new RegistryEntry
        {
            Definition = definition,
            SourcePack = sourcePack,
            SourcePath = sourcePath,
            IsLegacy = isLegacy
        };
    }

    private Dictionary<string, RegistryEntry> GetOrCreateKindBucket(ContentKind kind)
    {
        if (_entries.TryGetValue(kind, out var bucket))
            return bucket;

        bucket = new Dictionary<string, RegistryEntry>(StringComparer.OrdinalIgnoreCase);
        _entries[kind] = bucket;
        return bucket;
    }

    private static string NormalizeId(string id) => id.Trim();
}
