using openLuo.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Modules.SessionRuntime.Application;

public sealed class RuntimeBackedGameSession(
    IGameSessionRuntime runtime,
    SessionHandle handle,
    ISessionGameApi sessionApi) : IGameSession
{
    public string SessionId => handle.SessionId;

    public string ClientType => handle.ClientType;

    public string ClientId => handle.ClientId;

    public string? GameId => handle.GameId;

    public ISessionGameApi Api => sessionApi;

    public IAsyncEnumerable<GameEvent> StreamEventsAsync(CancellationToken ct = default) =>
        runtime.StreamEventsAsync(handle.SessionId, ct);

    public async Task<GameState?> TryGetStateAsync(CancellationToken ct = default)
    {
        var state = await runtime.TryGetStateAsync(handle.SessionId, ct);
        if (state is not null)
            handle.GameId = state.Id;
        return state;
    }

    public Task<IReadOnlyList<SessionCharacterRosterItem>> GetSessionRosterAsync(CancellationToken ct = default) =>
        runtime.GetSessionRosterAsync(handle.SessionId, ct);

    public Task<SessionCharacterStatusSnapshot?> GetCharacterStatusSnapshotAsync(string characterId, CancellationToken ct = default) =>
        runtime.GetCharacterStatusSnapshotAsync(handle.SessionId, characterId, ct);

    public Task<IReadOnlyList<SessionAttachment>> GetAttachmentsAsync(CancellationToken ct = default) =>
        runtime.GetAttachmentsAsync(handle.SessionId, ct);

    public Task<SessionAttachmentPayload?> GetAttachmentAsync(string attachmentId, CancellationToken ct = default) =>
        runtime.GetAttachmentAsync(handle.SessionId, attachmentId, ct);

    public Task<SessionAssetDescriptor?> GetAssetDescriptorAsync(string assetId, CancellationToken ct = default) =>
        runtime.GetAssetDescriptorAsync(handle.SessionId, assetId, ct);

    public Task<SessionAssetBlob?> GetAssetBlobAsync(string assetId, string blobRole = "primary", CancellationToken ct = default) =>
        runtime.GetAssetBlobAsync(handle.SessionId, assetId, blobRole, ct);

    public async Task<string> InitGameAsync(string archetypeId, string playerName, string? requestedGameId = null, CancellationToken ct = default)
    {
        var gameId = await runtime.InitGameAsync(handle.SessionId, archetypeId, playerName, requestedGameId, ct);
        handle.GameId = gameId;
        return gameId;
    }

    public Task<bool> SetActiveCharacterAsync(string characterId, CancellationToken ct = default) =>
        runtime.SetActiveCharacterAsync(handle.SessionId, characterId, ct);

    public Task<SessionSubmitResult> SubmitAsync(GameSessionInput input, CancellationToken ct = default) =>
        runtime.SubmitAsync(new SessionInput
        {
            SessionId = handle.SessionId,
            SourceId = input.SourceId,
            ChannelId = input.ChannelId,
            ActorId = input.ActorId,
            Kind = input.Kind,
            Text = input.Text,
            Command = input.Command,
            Parts = input.Parts,
            Origin = input.Origin,
            PresentationProfile = input.PresentationProfile,
            Arguments = input.Arguments,
            Metadata = input.Metadata,
            TimestampUtc = input.TimestampUtc
        }, ct);

    public Task CloseAsync(CancellationToken ct = default) =>
        runtime.CloseAsync(handle.SessionId, ct);
}
