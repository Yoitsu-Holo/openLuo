using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Validation;

public interface IContentValidator
{
    ContentValidationResult Validate(PackManifest manifest, IEnumerable<ContentDefinitionBase> definitions);
}
