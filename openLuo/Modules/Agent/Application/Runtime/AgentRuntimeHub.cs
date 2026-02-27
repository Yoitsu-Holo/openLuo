using System.Collections.Concurrent;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;

namespace openLuo.Modules.Agent.Application;

public interface IAgentRuntimeHub
{
    Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId,
        TimeSpan timeout,
        CancellationToken ct = default);

    Task EnsurePartyStartedAsync(string gameId, CancellationToken ct = default);

    Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId,
        TimeSpan timeout,
        IReadOnlyList<AgentContextBlock>? contextBlocks = null,
        CancellationToken ct = default);

    Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId,
        TimeSpan timeout,
        AgentExecutionContext? executionContext,
        IReadOnlyList<AgentContextBlock>? contextBlocks = null,
        IReadOnlyList<Block>? blocks = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentMessage>> RequestManyAsync(
        IReadOnlyList<string> characterIds,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string correlationId,
        TimeSpan timeout,
        CancellationToken ct = default);
}

public sealed class AgentRuntimeHub : IAgentRuntimeHub, IAsyncDisposable
{
    private readonly IAgentDispatcher _dispatcher;
    private readonly IAgentMessageHandler _handler;
    private readonly IAgentContextStore _contextStore;
    private readonly IAgentRoster _roster;
    private readonly IGameLogger _logger;
    private readonly ConcurrentDictionary<string, CharacterAgentRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);

    public AgentRuntimeHub(
        IAgentDispatcher dispatcher,
        IAgentMessageHandler handler,
        IAgentContextStore contextStore,
        IAgentRoster roster,
        IGameLogger logger)
    {
        _dispatcher = dispatcher;
        _handler = handler;
        _contextStore = contextStore;
        _roster = roster;
        _logger = logger;
    }

    public async Task EnsurePartyStartedAsync(string gameId, CancellationToken ct = default)
    {
        var characters = await _roster.ListAsync(gameId, ct);
        foreach (var character in characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id))
                continue;
            await EnsureRuntimeStartedAsync(NormalizeCharacterId(character.Id), ct);
        }
    }

    public async Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId,
        TimeSpan timeout,
        CancellationToken ct = default) =>
        await RequestAsync(characterId, type, from, payload, gameId, correlationId, timeout, contextBlocks: null, ct);

    public async Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId,
        TimeSpan timeout,
        IReadOnlyList<AgentContextBlock>? contextBlocks = null,
        CancellationToken ct = default)
    {
        var normalizedId = NormalizeCharacterId(characterId);
        await EnsureRuntimeStartedAsync(normalizedId, ct);

        var message = new AgentMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            GameId: gameId,
            From: from,
            To: normalizedId,
            Type: type,
            Payload: payload,
            CorrelationId: correlationId,
            TimestampUtc: DateTimeOffset.UtcNow,
            ExecutionContext: null,
            ContextBlocks: contextBlocks);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _dispatcher.RequestAsync(message, timeout, ct);
        sw.Stop();

        _logger.Debug("agent/dispatch", $"request {type} -> {normalizedId}", new
        {
            elapsedMs = sw.ElapsedMilliseconds,
            ok = response is not null,
            correlationId
        });

        return response;
    }

    public async Task<AgentMessage?> RequestAsync(
        string characterId,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string? correlationId,
        TimeSpan timeout,
        AgentExecutionContext? executionContext,
        IReadOnlyList<AgentContextBlock>? contextBlocks = null,
        IReadOnlyList<Block>? blocks = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var normalizedId = NormalizeCharacterId(characterId);
        await EnsureRuntimeStartedAsync(normalizedId, ct);

        executionContext?.ReportProgress($"dispatch:{type}:{normalizedId}");
        var message = new AgentMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            GameId: gameId,
            From: from,
            To: normalizedId,
            Type: type,
            Payload: payload,
            CorrelationId: correlationId,
            TimestampUtc: DateTimeOffset.UtcNow,
            ExecutionContext: executionContext,
            ContextBlocks: contextBlocks,
            Blocks: blocks,
            Metadata: metadata);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _dispatcher.RequestAsync(message, timeout, ct);
        sw.Stop();

        executionContext?.ReportProgress($"dispatch_reply:{type}:{normalizedId}");
        _logger.Debug("agent/dispatch", $"request {type} -> {normalizedId}", new
        {
            elapsedMs = sw.ElapsedMilliseconds,
            ok = response is not null,
            correlationId
        });

        return response;
    }

    public async Task<IReadOnlyList<AgentMessage>> RequestManyAsync(
        IReadOnlyList<string> characterIds,
        AgentMessageType type,
        string from,
        string payload,
        string gameId,
        string correlationId,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var tasks = characterIds
            .Select(id => RequestAsync(id, type, from, payload, gameId, correlationId, timeout, ct))
            .ToList();

        var responses = await Task.WhenAll(tasks);
        return responses.Where(x => x is not null).Select(x => x!).ToList();
    }

    private async Task EnsureRuntimeStartedAsync(string characterId, CancellationToken ct)
    {
        if (_runtimes.ContainsKey(characterId))
            return;

        var mailbox = new ChannelAgentMailbox(characterId);
        var runtime = new CharacterAgentRuntime(mailbox, _handler, _contextStore, _logger);

        if (_runtimes.TryAdd(characterId, runtime))
        {
            await _dispatcher.RegisterAsync(characterId, mailbox, ct);
            await runtime.StartAsync(ct);
            _logger.Info("agent/runtime", $"runtime started: {characterId}");
            return;
        }

        await runtime.StopAsync(ct);
    }

    private static string NormalizeCharacterId(string characterId)
    {
        var trimmed = characterId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "default" : trimmed.ToLowerInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (characterId, runtime) in _runtimes)
        {
            await runtime.StopAsync();
            await _dispatcher.UnregisterAsync(characterId);
        }
        _runtimes.Clear();
    }
}
