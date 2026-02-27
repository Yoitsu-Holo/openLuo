using openLuo.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IGameSession
{
    string SessionId { get; }

    string ClientType { get; }

    string ClientId { get; }

    string? GameId { get; }

    /// <summary>
    /// Session-scoped GameApi — frontend calls go through here.
    /// gameId is auto-resolved from the session binding.
    /// Methods present here = available to frontend; methods absent = blocked.
    /// </summary>
    ISessionGameApi Api { get; }

    IAsyncEnumerable<GameEvent> StreamEventsAsync(CancellationToken ct = default);

    Task<GameState?> TryGetStateAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SessionCharacterRosterItem>> GetSessionRosterAsync(CancellationToken ct = default);

    Task<SessionCharacterStatusSnapshot?> GetCharacterStatusSnapshotAsync(string characterId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionAttachment>> GetAttachmentsAsync(CancellationToken ct = default);

    Task<SessionAttachmentPayload?> GetAttachmentAsync(string attachmentId, CancellationToken ct = default);

    Task<SessionAssetDescriptor?> GetAssetDescriptorAsync(string assetId, CancellationToken ct = default);

    Task<SessionAssetBlob?> GetAssetBlobAsync(string assetId, string blobRole = "primary", CancellationToken ct = default);

    Task<string> InitGameAsync(string archetypeId, string playerName, string? requestedGameId = null, CancellationToken ct = default);

    Task<bool> SetActiveCharacterAsync(string characterId, CancellationToken ct = default);

    Task<SessionSubmitResult> SubmitAsync(GameSessionInput input, CancellationToken ct = default);

    Task CloseAsync(CancellationToken ct = default);
}
