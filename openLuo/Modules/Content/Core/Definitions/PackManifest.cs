namespace openLuo.Modules.Content.Core.Definitions;

public sealed class PackManifest : ContentDefinitionBase
{
    public override ContentKind Kind => ContentKind.PackManifest;

    public string Version { get; set; } = "1.0.0";
    public string ContentVersion { get; set; } = "1";
    public List<string> Authors { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
    public List<PackContentRef> Contents { get; set; } = [];
}

public sealed class PackContentRef
{
    public ContentKind Kind { get; set; }
    public string Id { get; set; } = string.Empty;
}
