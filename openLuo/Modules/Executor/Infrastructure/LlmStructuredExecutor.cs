using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;
using System.Diagnostics;

namespace openLuo.Modules.Executor.Infrastructure;

public abstract class LlmStructuredExecutor<TInput, TOutput> : IExecutor<TInput, TOutput>
{
    private readonly ILlmClient _llmClient;
    private readonly IExecutorPromptBuilder<TInput> _promptBuilder;
    private readonly IStructuredOutputParser _outputParser;
    private readonly IGameLogger? _logger;

    protected LlmStructuredExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<TInput> promptBuilder,
        IStructuredOutputParser outputParser,
        IGameLogger? logger = null)
    {
        _llmClient = llmClient;
        _promptBuilder = promptBuilder;
        _outputParser = outputParser;
        _logger = logger;
    }

    public abstract string Name { get; }

    public async Task<ExecutorResult<TOutput>> ExecuteAsync(TInput input, CancellationToken ct = default)
    {
        _logger?.Info("executor", $"executor start: {Name}");
        var sw = Stopwatch.StartNew();
        var prompt = _promptBuilder.Build(input);
        var raw = await _llmClient.CompleteAsync(prompt.Messages, prompt.Options, ct);
        var parsed = _outputParser.Parse<TOutput>(raw);
        sw.Stop();

        if (parsed.Success && parsed.Value is not null)
        {
            _logger?.Info("executor", $"executor done: {Name} [ok]", new
            {
                durationMs = sw.ElapsedMilliseconds,
                rawLength = raw.Length
            });
            return ExecutorResult<TOutput>.Ok(parsed.Value, raw);
        }

        _logger?.Warn("executor", $"executor done: {Name} [fail]", new
        {
            durationMs = sw.ElapsedMilliseconds,
            error = parsed.Error,
            rawLength = raw.Length
        });
        return ExecutorResult<TOutput>.Fail(parsed.Error, raw);
    }
}
