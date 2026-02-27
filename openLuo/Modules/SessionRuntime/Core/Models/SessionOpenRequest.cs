namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionOpenRequest
{
    public required string ClientType { get; init; }

    public required string ClientId { get; init; }

    public string? PreferredGameId { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];
}
