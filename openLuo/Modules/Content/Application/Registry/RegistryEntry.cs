using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Registry;

public sealed class RegistryEntry
{
    public required ContentDefinitionBase Definition { get; init; }
    public required string SourcePack { get; init; }
    public string? SourcePath { get; init; }
    public bool IsLegacy { get; init; }

    public ContentKind Kind => Definition.Kind;
    public string Id => Definition.Id;
}
