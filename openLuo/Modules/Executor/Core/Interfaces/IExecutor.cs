using openLuo.Modules.Executor.Core.Models;

namespace openLuo.Modules.Executor.Core.Interfaces;

/// <summary>
/// Executes one small structured task.
/// </summary>
public interface IExecutor<in TInput, TOutput>
{
    string Name { get; }

    Task<ExecutorResult<TOutput>> ExecuteAsync(TInput input, CancellationToken ct = default);
}
