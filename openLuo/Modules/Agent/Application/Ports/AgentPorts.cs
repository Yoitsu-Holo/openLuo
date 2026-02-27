using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.Agent.Application;

public interface IAgentProfileCatalog
{
    AgentProfile GetProfile(string characterId);
}

public interface ICosplaySkillProvider
{
    IReadOnlyList<SkillDocument> GetPreloadedSkills(AgentProfile profile);
}

public interface IAgentRoster
{
    Task<IReadOnlyList<Character>> ListAsync(string gameId, CancellationToken ct = default);

    Task<Character?> ResolveAsync(string gameId, string selector, CancellationToken ct = default);

    Task<Character?> GetActiveAsync(GameState state, CancellationToken ct = default);

    Task<Character?> SetActiveAsync(string gameId, string selector, CancellationToken ct = default);
}

public interface IAgentCommandBridge
{
    IReadOnlyList<CommandDescriptor> GetCommands();

    Task<CommandResult> ExecuteAsync(
        string commandName,
        string[] args,
        Dictionary<string, string> options,
        string characterId,
        GameBridgeRequestContext? context = null,
        string? category = null,
        CancellationToken ct = default);
}

public interface IAgentTaskStore
{
    Task<string> CreateTaskAsync(string gameId, string title, string requestedBy, string contextJson, CancellationToken ct = default);

    Task CreateStepsAsync(string taskId, IReadOnlyList<PartyTaskStepRecord> steps, CancellationToken ct = default);

    Task UpdateStepResultAsync(string stepId, string status, string resultJson, CancellationToken ct = default);

    Task UpdateTaskStatusAsync(string taskId, string status, CancellationToken ct = default);

    Task<PartyTaskRecord?> GetTaskAsync(string gameId, string taskId, CancellationToken ct = default);

    Task<IReadOnlyList<PartyTaskRecord>> ListRecentTasksAsync(string gameId, int limit = 5, CancellationToken ct = default);

    Task<IReadOnlyList<PartyTaskStepRecord>> ListStepsAsync(string taskId, CancellationToken ct = default);
}
