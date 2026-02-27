using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Executor.Application.GoalExecution;
using openLuo.Modules.Executor.Application.TODOList;
using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterExecNode
{
    private readonly IExecutor<GoalExecutorInput, GoalExecutorOutput> _goalExecutor;
    private readonly ICharacterToolGateway _toolGateway;
    private readonly CharacterResponseNode _responseNode;
    private readonly IGameLogger _logger;
    private readonly IRuntimeConfigCenter _config;

    private const int MaxAttemptsPerGoal = 3;

    public CharacterExecNode(
        IExecutor<GoalExecutorInput, GoalExecutorOutput> goalExecutor,
        ICharacterToolGateway toolGateway,
        CharacterResponseNode responseNode,
        IGameLogger logger,
        IRuntimeConfigCenter config)
    {
        _goalExecutor = goalExecutor;
        _toolGateway = toolGateway;
        _responseNode = responseNode;
        _logger = logger;
        _config = config;
    }

    public async Task<CharacterExecResult> ExecuteAsync(
        CharacterTurnContext context,
        TODOListOutput todoList,
        CancellationToken ct = default)
    {
        var allToolSteps = new List<AgentToolUseStep>();
        var allVisibleBlocks = new List<string>();
        var allPresentations = new List<CommandPresentation>();
        var capabilities = context.CapabilitySnapshot.Capabilities;

        var toolHistory = new List<string>();
        string? finalReply = null;

        foreach (var goal in todoList.Todos)
        {
            if (string.IsNullOrWhiteSpace(goal))
                continue;

            var goalAchieved = false;

            for (var attempt = 0; attempt < MaxAttemptsPerGoal && !goalAchieved; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var goalInput = BuildGoalInput(context, goal, capabilities, toolHistory);
                var goalResult = await _goalExecutor.ExecuteAsync(goalInput, ct);

                if (!goalResult.Success || goalResult.Output is null)
                {
                    _logger.Warn("exec/goal", $"GoalExecutor failed for '{goal}': {goalResult.Error}, skipping goal");
                    goalAchieved = true;
                    break;
                }

                var output = goalResult.Output;
                if (!string.Equals(output.Action, "call_tool", StringComparison.OrdinalIgnoreCase))
                {
                    goalAchieved = true;
                    break;
                }

                if (string.Equals(output.ToolName, "character_response", StringComparison.OrdinalIgnoreCase))
                {
                    var partialResult = BuildToolCallResult(allToolSteps, allVisibleBlocks, allPresentations);
                    var reply = await _responseNode.ExecuteAsync(context, partialResult, ct);

                    finalReply = reply;

                    toolHistory.Add($"SUCCESS: character_response: reply generated ({reply.Length} chars)");
                    allToolSteps.Add(new AgentToolUseStep
                    {
                        Iteration = attempt + 1,
                        Action = "call_tool",
                        Name = "character_response",
                        Success = true,
                        Summary = reply.Length > 80 ? reply[..80] + "..." : reply
                    });
                    goalAchieved = true;
                    break;
                }

                var capability = ResolveCapability(capabilities, output.ToolName);
                if (capability is null)
                {
                    toolHistory.Add($"ERROR: tool '{output.ToolName}' not found in available capabilities");
                    _logger.Warn("exec/goal", $"GoalExecutor selected unknown tool '{output.ToolName}' for goal '{goal}'");
                    continue;
                }

                var toolResult = await _toolGateway.ExecuteCapabilityDirectlyAsync(
                    context, capability, output.Args, output.Options, ct);

                allToolSteps.AddRange(toolResult.Steps);
                allVisibleBlocks.AddRange(toolResult.VisibleBlocks);
                if (toolResult.Presentation.Messages.Count > 0)
                    allPresentations.Add(toolResult.Presentation);

                if (toolResult.PendingAbility is not null || toolResult.EndDialogue)
                {
                    goalAchieved = true;
                    break;
                }

                var anySuccess = toolResult.Steps.Any(s => s.Success);
                if (anySuccess)
                {
                    var summary = toolResult.Steps.FirstOrDefault()?.Summary ?? "ok";
                    toolHistory.Add($"SUCCESS: {capability.Name}: {summary}");
                    goalAchieved = true;
                }
                else
                {
                    var error = toolResult.Steps.FirstOrDefault()?.Summary ?? "unknown error";
                    toolHistory.Add($"FAILED: {capability.Name}: {error}");
                    _logger.Info("exec/goal", $"tool attempt {attempt + 1}/{MaxAttemptsPerGoal} failed for '{goal}': {error}");
                }
            }

            if (!goalAchieved)
                _logger.Warn("exec/goal", $"Goal '{goal}' exceeded max attempts ({MaxAttemptsPerGoal}), moving to next goal");
        }

        var toolCallResult = BuildToolCallResult(allToolSteps, allVisibleBlocks, allPresentations);

        if (finalReply is null)
        {
            finalReply = await _responseNode.ExecuteAsync(context, toolCallResult, ct);
        }

        var presentation = BuildMergedPresentation(allPresentations, finalReply);

        return new CharacterExecResult
        {
            Steps = allToolSteps,
            ToolResult = toolCallResult,
            FinalReply = finalReply,
            VisibleBlocks = allVisibleBlocks,
            Presentation = presentation
        };
    }

    private GoalExecutorInput BuildGoalInput(
        CharacterTurnContext context,
        string goal,
        IReadOnlyList<AgentCapabilityDescriptor> capabilities,
        IReadOnlyList<string> toolHistory)
    {
        var executors = _config.GetSnapshot().Executors;
        return new GoalExecutorInput
        {
            Temperature = executors.GoalExecution.Temperature,
            MaxTokens = executors.GoalExecution.MaxTokens,
            CharacterProfile = context.PromptContext.CharacterProfile,
            SceneState = context.PromptContext.SceneState,
            Goal = goal,
            AvailableTools = capabilities
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Where(c => !string.Equals(c.Name, "narrative_chat", StringComparison.OrdinalIgnoreCase))
                .Select(c => new GoalExecutorCapability
                {
                    Name = c.Name,
                    Help = c.HelpShort,
                    Usage = c.Usage
                })
                .ToList(),
            ToolExecutionHistory = toolHistory
        };
    }

    private static AgentCapabilityDescriptor? ResolveCapability(
        IReadOnlyList<AgentCapabilityDescriptor> capabilities, string toolName)
    {
        return capabilities.FirstOrDefault(
            c => string.Equals(c.Name, toolName, StringComparison.OrdinalIgnoreCase)
                 || c.Aliases.Any(a => string.Equals(a, toolName, StringComparison.OrdinalIgnoreCase)));
    }

    private static CharacterToolCallResult BuildToolCallResult(
        IReadOnlyList<AgentToolUseStep> steps,
        IReadOnlyList<string> visibleBlocks,
        IReadOnlyList<CommandPresentation> presentations)
    {
        var mergedPresentation = CommandPresentation.Empty;
        if (presentations.Count > 0)
        {
            var messages = presentations
                .Where(p => p.Messages.Count > 0)
                .SelectMany(p => p.Messages)
                .ToList();
            if (messages.Count > 0)
                mergedPresentation = new CommandPresentation { Messages = messages };
        }

        return new CharacterToolCallResult
        {
            Steps = steps,
            VisibleBlocks = visibleBlocks,
            Presentation = mergedPresentation
        };
    }

    private static CommandPresentation BuildMergedPresentation(
        IReadOnlyList<CommandPresentation> presentations,
        string finalReply)
    {
        var toolMessages = presentations
            .Where(p => p.Messages.Count > 0)
            .SelectMany(p => p.Messages)
            .ToList();

        if (toolMessages.Count == 0)
            return string.IsNullOrWhiteSpace(finalReply)
                ? CommandPresentation.Empty
                : CommandPresentation.FromText(finalReply);

        if (string.IsNullOrWhiteSpace(finalReply))
            return new CommandPresentation { Messages = toolMessages };

        var replyMessage = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            SpeakerRole = "character",
            Blocks =
            [
                new TextBlock
                {
                    Kind = BlockKind.Text,
                    Visibility = OutputVisibility.Public,
                    Text = finalReply.Trim()
                }
            ]
        };

        var messages = new List<Message> { replyMessage };
        messages.AddRange(toolMessages);
        return new CommandPresentation { Messages = messages };
    }
}

public sealed class CharacterExecResult
{
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; } = [];
    public CharacterToolCallResult ToolResult { get; init; } = new();
    public string FinalReply { get; init; } = string.Empty;
    public IReadOnlyList<string> VisibleBlocks { get; init; } = [];
    public CommandPresentation Presentation { get; init; } = CommandPresentation.Empty;
}
