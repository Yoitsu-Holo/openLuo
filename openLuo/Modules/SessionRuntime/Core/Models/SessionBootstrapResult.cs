namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionBootstrapResult
{
    public required string SessionId { get; init; }

    public string GameId { get; init; } = string.Empty;

    public string ArchetypeId { get; init; } = string.Empty;

    public IReadOnlyList<SessionBootstrapCharacter> Characters { get; init; } = [];

    public string? ActiveCharacterId { get; init; }

    public IReadOnlyDictionary<string, SessionBootstrapResourceState> Resources { get; init; } =
        new Dictionary<string, SessionBootstrapResourceState>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SessionBootstrapDiagnostic> Diagnostics { get; init; } = [];
}

public sealed class SessionBootstrapCharacter
{
    public required string CharacterId { get; init; }

    public required string ArchetypeId { get; init; }

    public required string DisplayName { get; init; }

    public string InitialLocation { get; init; } = string.Empty;

    public string SourcePack { get; init; } = string.Empty;
}

public sealed class SessionBootstrapResourceState
{
    public required string ResourceId { get; init; }

    public required string DisplayName { get; init; }

    public string ResourceType { get; init; } = string.Empty;

    public decimal? Value { get; init; }

    public decimal? MinValue { get; init; }

    public decimal? MaxValue { get; init; }

    public string OwnerKind { get; init; } = string.Empty;

    public string SourcePack { get; init; } = string.Empty;
}

public sealed class SessionBootstrapDiagnostic
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
