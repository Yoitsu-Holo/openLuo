using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Interfaces.QQbot;

internal sealed class QqNormalizedMessage
{
    public required string Text { get; init; }
    public IReadOnlyList<SessionInputPart> Parts { get; init; } = [];
}
