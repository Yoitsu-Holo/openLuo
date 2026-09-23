using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;
using openLuo.Modules.WorldState.Application.Services;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.playgraound.Demos.Agent;

/// <summary>
/// 消息级增强（EnhanceChat）最小 E2E：
///   存储层 AgentConversationTurn.Metadata（键值对，永不进正文）
///   → 渲染层 ChatTagRenderer 白名单渲染 → AgentConversationMessage.Tags
///   → LLM 序列化点（BuildView 的 conversation record）拼接 [TIME:] + [TYPE:] 标记
///
/// 验证三条原则：
///   1. Content 保持原文（不含任何标记）
///   2. Metadata 只在序列化点渲染，未知 key 不进入 LLM
///   3. 时间标记与扩展标记按稳定顺序拼接
/// </summary>
internal static class EnhanceChatDemo
{
    public static async Task<int> RunAsync()
    {
        var manager = new AgentContextManager(
        [
            new TodoListContextRegistration(),
            new CharacterResponseContextRegistration(),
            new StateUpdateContextRegistration()
        ], timeService: new VirtualTimeService());

        var context = new AgentContext { GameId = "playground-enhance-chat", CharacterId = "rin" };
        context.Conversation.Add(new AgentConversationTurn
        {
            SpeakerRole = "inbound",
            SpeakerId = "2636124387",
            SpeakerName = "顽固幺鸡",
            Content = "转发了一张卡片",
            TimestampUtc = DateTimeOffset.UtcNow,
            GameDay = 1,
            GameMinute = 480,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = "card",
                ["source"] = "group",
                ["internal_note"] = "secret"
            }
        });
        context.Conversation.Add(new AgentConversationTurn
        {
            SpeakerRole = "outbound",
            SpeakerId = "rin",
            Content = "我看看这是什么。",
            TimestampUtc = DateTimeOffset.UtcNow,
            GameDay = 1,
            GameMinute = 480
        });

        var request = new CharacterTurnRequest
        {
            Context = context,
            Profile = new AgentProfile { CharacterId = "rin", DisplayName = "汐泠" },
            Message = new AgentMessage(
                MessageId: "msg-enhance-chat",
                GameId: "playground-enhance-chat",
                From: "player",
                To: "rin",
                Type: AgentMessageType.Chat,
                Payload: "那是什么",
                CorrelationId: "turn-enhance-chat",
                TimestampUtc: DateTimeOffset.UtcNow),
            Memory = new CharacterMemorySnapshot()
        };

        Console.WriteLine("=== EnhanceChat (Message-level Tags) Demo ===");
        Console.WriteLine("flow: turn.Metadata -> ChatTagRenderer -> message.Tags -> LLM serialization");
        Console.WriteLine();

        // ── 1. 存储层 → 渲染层 ──
        var prompt = await manager.BuildPromptContextAsync(
            request, request.Memory, new AgentCapabilitySnapshot(), string.Empty);

        var cardMessage = prompt.Conversation.First(message => message.Content.Contains("转发了一张卡片"));
        // 时间保持结构化（TimestampUtc/GameDay/GameMinute），渲染只发生在序列化点
        var serializedTime = TimeContextFormatter.FormatMessageTime(
            cardMessage.TimestampUtc, cardMessage.GameDay, cardMessage.GameMinute, TimeMode.Virtual);
        Console.WriteLine("--- message (rendered) ---");
        Console.WriteLine($"  content (raw):  {cardMessage.Content}");
        Console.WriteLine($"  structured:      day={cardMessage.GameDay} minute={cardMessage.GameMinute}");
        Console.WriteLine($"  timeTag:        {serializedTime}");
        Console.WriteLine($"  tags:           {string.Join(" ", cardMessage.Tags)}");
        Console.WriteLine($"  unknown key:     internal_note NOT rendered (whitelist)");

        Expect(!cardMessage.Content.Contains("[TYPE:"), "raw content must not contain rendered tags");
        Expect(cardMessage.Tags.Contains("[TYPE: card]"), "type metadata rendered as [TYPE: card]");
                Expect(cardMessage.Tags.All(tag => !tag.Contains("internal_note")), "unknown metadata keys stay out of LLM view");
        Console.WriteLine();

        // ── 2. 渲染层 → LLM 序列化（todo_list 的 conversation record）──
        var turn = BuildTurnContext(request, prompt);
        var view = manager.BuildView(new AgentContextBuildRequest
        {
            ExecutorId = AgentExecutorIds.TodoList,
            TurnContext = turn
        });

        Console.WriteLine("--- todo_list conversation records (LLM input) ---");
        var historyRecords = view.Regions.TryGetValue(AgentContextRegion.ConversationHistory, out var records)
            ? records
            : [];
        foreach (var record in historyRecords)
            Console.WriteLine($"  {record.Content}");
        Console.WriteLine();

        var cardRecord = historyRecords
            .First(record => record.Content.Contains("转发了一张卡片"));
        Expect(cardRecord.Content.StartsWith("[TIME: ", StringComparison.Ordinal)
                && cardRecord.Content.Contains("[TYPE: card]", StringComparison.Ordinal)
                && cardRecord.Content.EndsWith("转发了一张卡片", StringComparison.Ordinal),
            "serialized record composes time tag + extend tags before raw content");

        Console.WriteLine("=== PASS: EnhanceChat pipeline works end-to-end ===");
        return 0;
    }

    private static CharacterTurnContext BuildTurnContext(CharacterTurnRequest request, CharacterPromptContext prompt) => new()
    {
        Request = request,
        Profile = new CharacterAgentProfile { CharacterId = "rin", DisplayName = "汐泠" },
        State = new CharacterAgentState(),
        Memory = new CharacterMemorySnapshot { Summary = string.Empty },
        CurrentStateSummary = string.Empty,
        CapabilitySnapshot = new AgentCapabilitySnapshot(),
        PromptContext = prompt
    };

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException($"EnhanceChatDemo failed: {description}");
    }

    private sealed class VirtualTimeService : ITimeService
    {
        public Task<TimeSnapshot?> GetSnapshotAsync(string gameId, CancellationToken ct = default) =>
            Task.FromResult<TimeSnapshot?>(new TimeSnapshot { Day = 1, Minute = 480, Mode = TimeMode.Virtual });

        public Task<TimeSnapshot?> GetSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult<TimeSnapshot?>(new TimeSnapshot { Day = 1, Minute = 480, Mode = TimeMode.Virtual });

        public Task<TimeAdvanceResult> AdvanceAsync(int minutes, string source = "unknown", CancellationToken ct = default) =>
            Task.FromResult(new TimeAdvanceResult());

        public Task<TimeAdvanceResult> AdvanceAsync(string gameId, int minutes, string source = "unknown", CancellationToken ct = default) =>
            Task.FromResult(new TimeAdvanceResult());

        public Task<TimeSnapshot?> TickAsync(CancellationToken ct = default) =>
            Task.FromResult<TimeSnapshot?>(null);

        public Task<TimeSnapshot?> TickAsync(string gameId, CancellationToken ct = default) =>
            Task.FromResult<TimeSnapshot?>(null);
    }
}
