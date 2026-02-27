using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Validation;

public sealed class BasicContentValidator : IContentValidator
{
    public ContentValidationResult Validate(PackManifest manifest, IEnumerable<ContentDefinitionBase> definitions)
    {
        var result = new ContentValidationResult();
        ValidateManifest(manifest, result);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            ValidateDefinition(definition, result);

            var key = BuildKey(definition.Kind, definition.Id);
            if (!seen.Add(key))
                result.AddError(definition.Kind, definition.Id, "Duplicate content id in pack.");
        }

        foreach (var contentRef in manifest.Contents)
        {
            if (string.IsNullOrWhiteSpace(contentRef.Id))
            {
                result.AddError(ContentKind.PackManifest, manifest.Id, "Pack content reference id is required.");
                continue;
            }

            if (!seen.Contains(BuildKey(contentRef.Kind, contentRef.Id)))
                result.AddWarning(ContentKind.PackManifest, manifest.Id, $"Referenced content '{contentRef.Kind}:{contentRef.Id}' was not loaded.");
        }

        return result;
    }

    private static void ValidateManifest(PackManifest manifest, ContentValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            result.AddError(ContentKind.PackManifest, string.Empty, "Pack manifest id is required.");
        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
            result.AddError(ContentKind.PackManifest, manifest.Id, "Pack manifest display name is required.");
    }

    private static void ValidateDefinition(ContentDefinitionBase definition, ContentValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            result.AddError(definition.Kind, string.Empty, "Definition id is required.");
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            result.AddError(definition.Kind, definition.Id, "Definition display name is required.");

        switch (definition)
        {
            case CharacterArchetypeDefinition character when string.IsNullOrWhiteSpace(character.CharacterName):
                result.AddError(character.Kind, character.Id, "Character name is required.");
                break;
            case ResourceDefinition resource when resource.MinValue.HasValue && resource.MaxValue.HasValue && resource.MinValue > resource.MaxValue:
                result.AddError(resource.Kind, resource.Id, "Resource min value cannot be greater than max value.");
                break;
            case ItemDefinition item when item.Price < 0:
                result.AddError(item.Kind, item.Id, "Item price cannot be negative.");
                break;
            case ToolDefinition tool when string.IsNullOrWhiteSpace(tool.EntryPoint):
                result.AddWarning(tool.Kind, tool.Id, "Tool entry point is empty.");
                break;
            case SkillDefinition skill when string.IsNullOrWhiteSpace(skill.Body):
                result.AddWarning(skill.Kind, skill.Id, "Skill body is empty.");
                break;
        }
    }

    private static string BuildKey(ContentKind kind, string id) =>
        $"{kind}:{id}".Trim().ToLowerInvariant();
}
