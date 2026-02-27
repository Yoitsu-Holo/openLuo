using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Validation;

public sealed class ContentValidationResult
{
    public List<ContentValidationIssue> Issues { get; } = [];

    public bool IsValid => Issues.TrueForAll(issue => issue.Severity is not ContentValidationSeverity.Error);

    public void AddError(ContentKind kind, string id, string message) =>
        Issues.Add(new ContentValidationIssue(ContentValidationSeverity.Error, kind, id, message));

    public void AddWarning(ContentKind kind, string id, string message) =>
        Issues.Add(new ContentValidationIssue(ContentValidationSeverity.Warning, kind, id, message));
}

public sealed record ContentValidationIssue(
    ContentValidationSeverity Severity,
    ContentKind Kind,
    string Id,
    string Message);

public enum ContentValidationSeverity
{
    Warning,
    Error
}
