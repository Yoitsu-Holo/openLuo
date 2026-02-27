namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionHandle
{
    public required string SessionId { get; init; }

    public required string ClientType { get; init; }

    public required string ClientId { get; init; }

    public string? GameId { get; set; }
}
