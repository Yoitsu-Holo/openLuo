using openLuo.Modules.Agent.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface IPartyTaskRepository
{
    Task<string> CreateTaskAsync(string gameId, string title, string requestedBy, string contextJson, CancellationToken ct = default);

    Task CreateStepsAsync(string taskId, IReadOnlyList<PartyTaskStepRecord> steps, CancellationToken ct = default);

    Task UpdateStepResultAsync(string stepId, string status, string resultJson, CancellationToken ct = default);

    Task UpdateTaskStatusAsync(string taskId, string status, CancellationToken ct = default);

    Task<PartyTaskRecord?> GetTaskAsync(string gameId, string taskId, CancellationToken ct = default);

    Task<IReadOnlyList<PartyTaskRecord>> ListRecentTasksAsync(string gameId, int limit = 5, CancellationToken ct = default);

    Task<IReadOnlyList<PartyTaskStepRecord>> ListStepsAsync(string taskId, CancellationToken ct = default);
}
