using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Modules.Memory.Core.Interfaces;
using System.Diagnostics;

namespace openLuo.Modules.Executor.Application.MemoryRecall;

public sealed class MemoryRecallExecutor : IExecutor<MemoryRecallInput, MemoryRecallOutput>
{
    private readonly IMemoryQueryProjector _queryProjector;
    private readonly IMemoryRecallService _recallService;
    private readonly IMemoryRecallFormatter _formatter;
    private readonly IGameLogger? _logger;

    public MemoryRecallExecutor(
        IMemoryQueryProjector queryProjector,
        IMemoryRecallService recallService,
        IMemoryRecallFormatter formatter,
        IGameLogger? logger = null)
    {
        _queryProjector = queryProjector;
        _recallService = recallService;
        _formatter = formatter;
        _logger = logger;
    }

    public string Name => "memoryRecall";

    public async Task<ExecutorResult<MemoryRecallOutput>> ExecuteAsync(MemoryRecallInput input, CancellationToken ct = default)
    {
        _logger?.Info("executor", $"executor start: {Name}");
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(input.GameId))
        {
            sw.Stop();
            _logger?.Warn("executor", $"executor done: {Name} [fail]", new { durationMs = sw.ElapsedMilliseconds, error = "GameId is required." });
            return ExecutorResult<MemoryRecallOutput>.Fail("GameId is required.");
        }
        if (string.IsNullOrWhiteSpace(input.CharacterId))
        {
            sw.Stop();
            _logger?.Warn("executor", $"executor done: {Name} [fail]", new { durationMs = sw.ElapsedMilliseconds, error = "CharacterId is required." });
            return ExecutorResult<MemoryRecallOutput>.Fail("CharacterId is required.");
        }

        var query = await _queryProjector.ProjectAsync(input, ct);
        var recall = await _recallService.RecallAsync(query, ct);
        var formatted = _formatter.Format(recall.Records, input.Options);
        sw.Stop();

        _logger?.Info("executor", $"executor done: {Name} [ok]", new
        {
            durationMs = sw.ElapsedMilliseconds,
            degraded = recall.Degraded,
            snippetCount = formatted.Snippets.Count
        });
        return ExecutorResult<MemoryRecallOutput>.Ok(new MemoryRecallOutput
        {
            Query = query,
            MemorySnippets = formatted.Snippets,
            MemorySummary = string.IsNullOrWhiteSpace(recall.Summary) ? formatted.Summary : recall.Summary,
            RetrievalTrace = recall.Trace,
            Degraded = recall.Degraded
        });
    }
}
