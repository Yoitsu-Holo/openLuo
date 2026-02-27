namespace openLuo.Modules.SessionRuntime.Core.Models;

public sealed class SessionBootstrapRequest
{
    public required string SessionId { get; init; }

    public IReadOnlyList<string> SelectedCharacterIds { get; init; } = [];

    public string? ActiveCharacterId { get; init; }

    public string PlayerName { get; init; } = string.Empty;

    public IReadOnlyList<string> SharedMemorySeeds { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> PrivateMemorySeedsByCharacter { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, decimal?> ResourceOverrides { get; init; } =
        new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
}
