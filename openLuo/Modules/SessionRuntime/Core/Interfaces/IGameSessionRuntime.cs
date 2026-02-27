using openLuo.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Core.Interfaces;

public interface IGameSessionRuntime
{
    Task<SessionHandle> OpenAsync(SessionOpenRequest request, CancellationToken ct = default);

    Task CloseAsync(string sessionId, CancellationToken ct = default);

    IAsyncEnumerable<GameEvent> StreamEventsAsync(string sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionGameEntry>> GetGameIdsAsync(CancellationToken ct = default);

    Task<GameState?> TryGetStateAsync(string sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionArchetypeOption>> GetAvailableArchetypesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SessionCharacterRosterItem>> GetSessionRosterAsync(string sessionId, CancellationToken ct = default);

    Task<SessionCharacterStatusSnapshot?> GetCharacterStatusSnapshotAsync(string sessionId, string characterId, CancellationToken ct = default);

    Task<IReadOnlyList<SessionAttachment>> GetAttachmentsAsync(string sessionId, CancellationToken ct = default);

    Task<SessionAttachmentPayload?> GetAttachmentAsync(string sessionId, string attachmentId, CancellationToken ct = default);

    Task<SessionAssetDescriptor?> GetAssetDescriptorAsync(string sessionId, string assetId, CancellationToken ct = default);

    Task<SessionAssetBlob?> GetAssetBlobAsync(string sessionId, string assetId, string blobRole = "primary", CancellationToken ct = default);

    Task<string> InitGameAsync(string sessionId, string archetypeId, string playerName, string? requestedGameId = null, CancellationToken ct = default);

    Task InitializeGameAsync(string sessionId, string archetypeId, string playerName, CancellationToken ct = default);

    Task<bool> SetActiveCharacterAsync(string sessionId, string characterId, CancellationToken ct = default);

    Task<SessionSubmitResult> SubmitAsync(SessionInput input, CancellationToken ct = default);
}
