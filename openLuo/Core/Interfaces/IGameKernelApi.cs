using openLuo.Core.Models;
using System.Text.Json.Nodes;

namespace openLuo.Core.Interfaces;

/// <summary>
/// Kernel-level typed API for engine primitives.
/// Methods here correspond to core/ routes and are guaranteed to exist
/// regardless of which plugins are loaded.
/// 
/// Kernel = task orchestration + plugin invocation + context management + routing control.
/// No gameplay content lives here.
/// </summary>
public interface IGameKernelApi
{
    // ── Archive management ──────────────────────────────────────────────

    /// <summary>List all existing game saves.</summary>
    Task<IReadOnlyList<GameEntry>> ListGamesAsync(CancellationToken ct = default);

    /// <summary>List all available character archetypes.</summary>
    Task<IReadOnlyList<ArchetypeInfo>> ListArchetypesAsync(CancellationToken ct = default);

    /// <summary>Create a new game and return its gameId.</summary>
    Task<CreateGameResult> CreateGameAsync(CreateGameRequest req, CancellationToken ct = default);

    // ── Chat ────────────────────────────────────────────────────────────

    /// <summary>Send a chat message to the active character.</summary>
    Task<SendMessageResult> SendMessageAsync(SendMessageRequest req, CancellationToken ct = default);


    // ── Command ──────────────────────────────────────────────────────────

    /// <summary>Execute a slash command. Falls back to plugin routing where applicable.</summary>
    Task<ExecuteCommandResult> ExecuteCommandAsync(CommandRequest req, CancellationToken ct = default);
    // ── core/session/* ──────────────────────────────────────────────────

    /// <summary>Get full game status snapshot (session, time, characters).</summary>
    Task<GameStatus> GetSessionAsync(GetStateRequest req, CancellationToken ct = default);

    // ── core/time/* ─────────────────────────────────────────────────────

    /// <summary>Get current game time.</summary>
    Task<JsonNode?> GetTimeAsync(string gameId, CancellationToken ct = default);

    /// <summary>Advance game time by the given number of minutes.</summary>
    Task<JsonNode?> AdvanceTimeAsync(string gameId, int minutes, CancellationToken ct = default);

    // ── core/state/* ────────────────────────────────────────────────────

    /// <summary>Register a new state definition.</summary>
    Task<JsonNode?> RegisterStateDefAsync(JsonNode? def, CancellationToken ct = default);

    /// <summary>Get a single state value by namespace + key + owner.</summary>
    Task<JsonNode?> GetStateValueAsync(string gameId, string @namespace, string key, string ownerKind, string ownerId, CancellationToken ct = default);

    /// <summary>Query state values with optional filters.</summary>
    Task<JsonNode?> QueryStatesAsync(string gameId, JsonNode? queryParams, CancellationToken ct = default);

    /// <summary>Apply a batch of state mutations atomically.</summary>
    Task<JsonNode?> ApplyStatesAsync(string gameId, JsonNode? mutations, CancellationToken ct = default);

    // ── core/character/* ────────────────────────────────────────────────

    /// <summary>Get current active character info.</summary>
    Task<JsonNode?> GetCharacterAsync(string gameId, CancellationToken ct = default);
}
