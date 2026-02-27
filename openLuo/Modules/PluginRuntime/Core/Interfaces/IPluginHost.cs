using openLuo.Application.Models.StateEvaluation;
using openLuo.Modules.Agent.Core.Models.Flow;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Modules.PluginRuntime.Core.Interfaces;

/// <summary>
/// Manages plugin lifecycle, hook execution, and dynamic command registration.
/// Plugins are loaded from external processes and communicate via JSON-RPC over stdio.
/// </summary>
public interface IPluginHost : IAsyncDisposable
{
    /// <summary>
    /// Load all plugins from a directory.
    /// </summary>
    Task LoadAllAsync(string pluginsDir, CancellationToken ct = default);

    /// <summary>
    /// Call a hook on all loaded plugins and aggregate results.
    /// </summary>
    Task<PluginHookResult> CallHookAsync(string hookName, object args, CancellationToken ct = default, GameBridgeRequestContext? context = null);

    /// <summary>
    /// Call onPromptContext hook and collect prompt fragments from all plugins.
    /// </summary>
    Task<List<PromptFragment>> CallPromptContextHookAsync(OnPromptContextInput input, CancellationToken ct = default);

    /// <summary>
    /// Call onStatusQuery hook and collect status items from all plugins.
    /// </summary>
    Task<List<StatusItem>> CallStatusQueryHookAsync(OnStatusQueryInput input, CancellationToken ct = default);

    /// <summary>
    /// Call onChatAfter hook and collect passive post-chat notices.
    /// </summary>
    Task<OnChatAfterOutput> CallChatAfterHookAsync(OnChatAfterInput input, CancellationToken ct = default);

    /// <summary>
    /// Call onToolExecuted hook and collect passive post-tool notices.
    /// </summary>
    Task<OnToolExecutedOutput> CallToolExecutedHookAsync(OnToolExecutedInput input, CancellationToken ct = default);

    /// <summary>
    /// Get all commands registered by loaded plugins.
    /// </summary>
    IReadOnlyList<CommandDescriptor> GetRegisteredCommands();

    /// <summary>
    /// Get all flow registrations declared by loaded plugins.
    /// </summary>
    IReadOnlyList<AgentFlowRegistration> GetRegisteredFlows();

    /// <summary>
    /// Execute a plugin command routed from GameEngine.
    /// </summary>
    Task<CommandResult> ExecutePluginCommandAsync(string commandName, object args, CancellationToken ct = default, string? category = null, GameBridgeRequestContext? context = null);
}

/// <summary>
/// Aggregated result from hook execution across all plugins.
/// </summary>
public class PluginHookResult
{
    /// <summary>Additional text to append to narrative output.</summary>
    public string? AdditionalText { get; set; }

    /// <summary>Extra system-level prompt text to append before LLM generation.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Memory content to store for the character.</summary>
    public string? MemoryToStore { get; set; }

    /// <summary>Affection point delta to apply.</summary>
    public int? AffectionDelta { get; set; }

    /// <summary>Affection multiplier (e.g., 2 = double affection gain).</summary>
    public int? AffectionMultiplier { get; set; }

    /// <summary>Stamina bonus to apply.</summary>
    public int? StaminaBonus { get; set; }

    /// <summary>Dream text to display in sleep phase.</summary>
    public string? DreamText { get; set; }

    /// <summary>Modified LLM prompt for narrative generation.</summary>
    public string? ModifiedPrompt { get; set; }

    /// <summary>If true, cancel the current operation.</summary>
    public bool Cancel { get; set; }
}
