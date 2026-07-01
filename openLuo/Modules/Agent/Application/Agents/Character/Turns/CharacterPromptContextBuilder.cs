using openLuo.Core.Models;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.Agent.Application;

public sealed class CharacterPromptContextBuilder : ICharacterPromptContextBuilder
{
    private const int MaxConversationMessages = 24;
    private static readonly TimeSpan RealtimeConversationWindow = TimeSpan.FromMinutes(15);

    private readonly ITimeService _timeService;
    private readonly IRuntimeConfigCenter _configCenter;

    public CharacterPromptContextBuilder(ITimeService timeService, IRuntimeConfigCenter configCenter)
    {
        _timeService = timeService;
        _configCenter = configCenter;
    }

    public async Task<CharacterPromptContext> BuildAsync(
        CharacterTurnRequest request,
        CharacterMemorySnapshot memory,
        AgentCapabilitySnapshot capabilitySnapshot,
        string currentStateSummary,
        CancellationToken ct = default)
    {
        var extraContexts = request.ExtraContexts.Where(x => !string.IsNullOrWhiteSpace(x.Content)).ToList();
        var timeSnapshot = await _timeService.GetSnapshotAsync(request.Context.GameId, ct);
        var config = _configCenter.GetSnapshot();
        var hasImages = request.Message.Blocks?.Any(b => b is ImageBlock) == true;

        var playerInput = request.Message.Payload;
        if (hasImages)
        {
            if (config.Llm.SupportsVision)
                playerInput = $"[当前消息包含用户发送的图片。你可以直接观察图片内容并基于图片回复。]\n{playerInput}";
            else
                playerInput = $"[当前消息包含用户发送的图片。当前模型不支持视觉识别，请向用户说明无法查看图片。]\n{playerInput}";
        }

        return new CharacterPromptContext
        {
            CharacterProfile = request.Profile.RolePrompt,
            WorldContext = JoinByRule(extraContexts, EnhanceMessageRule.WorldContext),
            SceneState = JoinByRule(extraContexts, EnhanceMessageRule.SceneState),
            GoalContext = JoinByRule(extraContexts, EnhanceMessageRule.GoalContext),
            CurrentStateSummary = currentStateSummary,
            AvailableTools = capabilitySnapshot.Capabilities.Select(x => x.Name).ToList(),
            ToolCatalog = capabilitySnapshot.Capabilities
                .Select(cap =>
                {
                    var desc = cap.HelpShort;
                    if (!string.IsNullOrWhiteSpace(cap.Usage))
                        desc += $"（{cap.Usage}）";
                    return $"{cap.Name}: {desc}";
                })
                .ToList(),
            Conversation = BuildConversation(request, timeSnapshot),
            ExtraContexts = extraContexts,
            PlayerInput = playerInput,
            PlayerBlocks = request.Message.Blocks
        };
    }

    private static IReadOnlyList<AgentConversationMessage> BuildConversation(
        CharacterTurnRequest request,
        TimeSnapshot? timeSnapshot)
    {
        IEnumerable<AgentConversationTurn> turns = request.Context.Conversation;
        if (timeSnapshot?.Mode == TimeMode.Realtime)
        {
            var cutoff = DateTimeOffset.UtcNow - RealtimeConversationWindow;
            turns = turns.Where(turn => turn.TimestampUtc >= cutoff);
        }

        var selectedTurns = turns.TakeLast(MaxConversationMessages).ToList();

        if (selectedTurns.Count > 0)
        {
            var last = selectedTurns[^1];
            var isInbound = !last.SpeakerRole.Equals("outbound", StringComparison.OrdinalIgnoreCase);
            if (isInbound && string.Equals(last.Content, request.Message.Payload, StringComparison.Ordinal))
                selectedTurns.RemoveAt(selectedTurns.Count - 1);
        }

        var messages = new List<AgentConversationMessage>();
        foreach (var turn in selectedTurns)
        {
            var role = turn.SpeakerRole.Equals("outbound", StringComparison.OrdinalIgnoreCase)
                ? AgentConversationRole.Assistant
                : AgentConversationRole.User;
            messages.Add(new AgentConversationMessage(role, turn.Content));
        }
        return messages;
    }

    private static string JoinByRule(IReadOnlyList<AgentContextBlock> blocks, EnhanceMessageRule rule) =>
        string.Join("\n", blocks.Where(x => x.Rule == rule).Select(x => x.Content.Trim()));
}
