using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core.Models;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认会话上下文（D15/D7/D44）。快照不可变；工具结果追加为新的 Snapshot（版本递增）。
/// 每会话独立实例。
/// <para>
/// 历史窗口（D44）：条数取自 <see cref="IContextLimitsProvider"/>，再按「限额 − 系统块占用」的
/// 剩余预算从**最新往前**累积裁剪（最旧一侧截断），并保证至少保留最新一条。
/// 被裁条数记入 <see cref="ContextBudgetSnapshot.ConversationTurnsDropped"/>，
/// 不再出现"历史无限增长却没人知道"的情况。
/// </para>
/// </summary>
public sealed class DefaultAgentContextSession : IAgentContextSession
{
    private readonly IContextAssembler _assembler;
    private readonly IConversationStore _conversationStore;
    private readonly IMessageTagPipeline _tagPipeline;
    private readonly ITokenCounter _counter;
    private readonly IContextLimitsProvider _limits;
    private readonly object _gate = new();

    private AgentDecisionContext? _current;
    private long _snapshotVersion;
    private List<ConversationTurn> _conversation = [];

    public DefaultAgentContextSession(
        string sessionId,
        string subjectId,
        IContextAssembler assembler,
        IConversationStore conversationStore,
        IMessageTagPipeline tagPipeline,
        ITokenCounter? counter = null,
        IContextLimitsProvider? limits = null,
        string? conversationId = null)
    {
        SessionId = sessionId;
        SubjectId = subjectId;
        ConversationId = string.IsNullOrWhiteSpace(conversationId) ? sessionId : conversationId;
        _assembler = assembler;
        _conversationStore = conversationStore;
        _tagPipeline = tagPipeline;
        _counter = counter ?? HeuristicTokenCounter.Instance;
        _limits = limits ?? StaticContextLimitsProvider.Default;
    }

    public string SessionId { get; }
    public string SubjectId { get; }

    /// <summary>会话键（多端共享同一段对话的支点）；域键的 Session 维度用它。</summary>
    public string ConversationId { get; }

    public AgentDecisionContext Current =>
        _current ?? throw new InvalidOperationException("Session snapshot has not been created yet.");

    public async Task<AgentDecisionContext> CreateTurnSnapshotAsync(
        ContextBuildRequest request,
        CancellationToken ct = default)
    {
        var built = await _assembler.BuildAsync(request, ct);
        var limits = _limits.Current;
        var conversation = await LoadWithinBudgetAsync(built.Budget, limits, ct);

        lock (_gate)
        {
            _conversation = conversation.Turns;
            _snapshotVersion++;
            _current = built with
            {
                SnapshotVersion = _snapshotVersion,
                Conversation = _conversation,
                Budget = built.Budget with
                {
                    // 会话是"窗口裁剪"的执行者，所以由它写回生效上限，保证账目里的 MaxTokens
                    // 永远等于本次实际采用的值（装配器可能返回自己那份快照）。
                    MaxTokens = limits.MaxTokens,
                    ConversationTokens = conversation.Tokens,
                    ConversationTurnsDropped = conversation.Dropped
                }
            };
        }
        return _current;
    }

    /// <summary>
    /// 读取对话历史并按剩余预算裁剪（最旧一侧截断）。至少保留最新一条：
    /// 「只剩一个人设、忘了刚才说了什么」比「略超预算」更糟。
    /// </summary>
    private async Task<(List<ConversationTurn> Turns, int Tokens, int Dropped)> LoadWithinBudgetAsync(
        ContextBudgetSnapshot systemBudget, ContextLimits limits, CancellationToken ct)
    {
        var retain = Math.Max(1, limits.ConversationRetainCount);
        var recent = await _conversationStore.GetRecentAsync(SessionId, retain, ct);
        var remaining = Math.Max(0, limits.MaxTokens - systemBudget.SystemTokens);

        var kept = new List<ConversationTurn>(recent.Count);
        var used = 0;
        var dropped = 0;

        for (var i = recent.Count - 1; i >= 0; i--)
        {
            var tokens = _counter.Count(recent[i].Content);
            if (kept.Count > 0 && used + tokens > remaining)
            {
                dropped = i + 1;
                break;
            }

            used += tokens;
            kept.Add(recent[i]);
        }

        kept.Reverse();
        return (kept, used, dropped);
    }

    public Task<AgentDecisionContext> ApplyToolResultsAsync(
        IReadOnlyList<CapabilityCall> calls,
        IReadOnlyList<CapabilityResult> results,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_current is null)
                throw new InvalidOperationException("Session snapshot has not been created yet.");

            _snapshotVersion++;

            // 追加 assistant 消息（带 tool_calls 声明）——OpenAI 协议要求 tool 消息前
            // 必须有引用同一 id 的 assistant tool_calls 消息（仅内存，不落库）。
            // 序列化格式与 LlmToolCall 对齐：[{Id, Name, ArgumentsJson}]。
            var toolCallNodes = new List<System.Text.Json.Nodes.JsonObject>();
            foreach (var call in calls)
            {
                if (string.IsNullOrWhiteSpace(call.ModelCallId))
                    continue;
                toolCallNodes.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["Id"] = call.ModelCallId,
                    ["Name"] = call.ModelToolName ?? call.CanonicalId,
                    ["ArgumentsJson"] = call.RawArgumentsJson ?? "{}"
                });
            }
            if (toolCallNodes.Count > 0)
            {
                _current = _current with
                {
                    SnapshotVersion = _snapshotVersion,
                    Conversation = [.. _current.Conversation, new ConversationTurn
                    {
                        SessionId = SessionId,
                        TurnId = _current.TurnId,
                        SpeakerId = SubjectId,
                        SpeakerRole = "assistant",
                        Content = string.Empty,
                        ToolCallsJson = System.Text.Json.JsonSerializer.Serialize(toolCallNodes),
                        TimestampUtc = DateTimeOffset.UtcNow
                    }]
                };
            }

            // 工具结果作为 tool role 消息追加到内存对话（不落库），
            // ToolCallId 引用模型侧 tool_call id，供 API 消息流回传（D17）。
            var toolMessages = new List<ConversationTurn>();
            foreach (var result in results)
            {
                var call = calls.FirstOrDefault(c => string.Equals(c.InvocationId, result.InvocationId, StringComparison.Ordinal));
                if (call is null || string.IsNullOrWhiteSpace(call.ModelCallId))
                    continue;
                toolMessages.Add(new ConversationTurn
                {
                    SessionId = SessionId,
                    TurnId = _current.TurnId,
                    SpeakerId = SubjectId,
                    SpeakerRole = "tool",
                    ToolCallId = call.ModelCallId,
                    Content = result.Text ?? result.Error ?? string.Empty,
                    TimestampUtc = DateTimeOffset.UtcNow
                });
            }

            var conversation = _current.Conversation.ToList();
            conversation.AddRange(toolMessages);

            _current = _current with
            {
                SnapshotVersion = _snapshotVersion,
                Conversation = conversation,
                // 本回合内追加的消息同样计入账目。回合内**不做裁剪**：
                // assistant(tool_calls) 与其后的 tool 消息必须成组出现，拆开会被模型 API 拒绝。
                Budget = _current.Budget with
                {
                    ConversationTokens = conversation.Sum(t => _counter.Count(t.Content))
                }
            };
            return Task.FromResult(_current);
        }
    }

    public Task CommitTurnAsync(TurnCompletion completion, CancellationToken ct = default)
    {
        var writes = new List<Task>();
        if (!string.IsNullOrWhiteSpace(completion.UserText))
        {
            writes.Add(_conversationStore.AppendAsync(new ConversationTurn
            {
                SessionId = SessionId,
                TurnId = $"{completion.TurnId}:user",
                // 稳定身份优先（D45）：平台给了 userId 就用它；没给才回退到会话主体。
                // 改造前这里写的是**显示名**——改名/同名即串味，且无法按人分区。
                SpeakerId = string.IsNullOrWhiteSpace(completion.UserId) ? SubjectId : completion.UserId!,
                SpeakerName = completion.SenderName ?? string.Empty,
                SpeakerRole = "inbound",
                ThreadId = completion.ThreadId,
                Content = completion.UserText,
                TimestampUtc = DateTimeOffset.UtcNow
            }, ct));
        }

        var text = completion.FinalText;
        if (string.IsNullOrWhiteSpace(text))
            text = completion.Outputs?.LastOrDefault(o => o.Kind == ReplyItemKind.Text)?.Payload?.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            writes.Add(_conversationStore.AppendAsync(new ConversationTurn
            {
                SessionId = SessionId,
                TurnId = $"{completion.TurnId}:assistant",
                SpeakerId = SubjectId,
                SpeakerName = SubjectId,
                SpeakerRole = "outbound",
                ThreadId = completion.ThreadId,
                Content = text,
                TimestampUtc = DateTimeOffset.UtcNow
            }, ct));
        }
        return writes.Count == 0 ? Task.CompletedTask : Task.WhenAll(writes);
    }
}
