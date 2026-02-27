using openLuo.Core.Interfaces;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Core.Models;
using openLuo.Modules.Llm.Core.Interfaces;
using System.Diagnostics;

namespace openLuo.Modules.Executor.Infrastructure;

public abstract class LlmTextExecutor<TInput> : IExecutor<TInput, string>
{
    private readonly ILlmClient _llmClient;
    private readonly IExecutorPromptBuilder<TInput> _promptBuilder;
    private readonly IGameLogger? _logger;

    protected LlmTextExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<TInput> promptBuilder,
        IGameLogger? logger = null)
    {
        _llmClient = llmClient;
        _promptBuilder = promptBuilder;
        _logger = logger;
    }

    public abstract string Name { get; }

    public async Task<ExecutorResult<string>> ExecuteAsync(TInput input, CancellationToken ct = default)
    {
        _logger?.Info("executor", $"executor start: {Name}");
        var sw = Stopwatch.StartNew();
        var prompt = _promptBuilder.Build(input);
        var raw = await _llmClient.CompleteAsync(prompt.Messages, prompt.Options, ct);
        var text = NormalizeOutput(raw);
        sw.Stop();

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger?.Warn("executor", $"executor done: {Name} [fail]", new
            {
                durationMs = sw.ElapsedMilliseconds,
                error = "Model output is empty.",
                rawLength = raw.Length
            });
            return ExecutorResult<string>.Fail("Model output is empty.", raw);
        }

        _logger?.Info("executor", $"executor done: {Name} [ok]", new
        {
            durationMs = sw.ElapsedMilliseconds,
            rawLength = raw.Length
        });
        return ExecutorResult<string>.Ok(text, raw);
    }

    protected virtual string NormalizeOutput(string raw) => raw.Trim();
}
