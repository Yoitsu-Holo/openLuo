using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core.Models;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认会话上下文（D15/D7）。快照不可变；工具结果追加为新的 Snapshot（版本递增）。
/// 每会话独立实例。
/// </summary>
public sealed class DefaultAgentContextSession : IAgentContextSession
{
    private readonly IContextAssembler _assembler;
    private readonly IConversationStore _conversationStore;
    private readonly IMessageTagPipeline _tagPipeline;
    private readonly object _gate = new();

    private AgentDecisionContext? _current;
    private long _snapshotVersion;
    private List<ConversationTurn> _conversation = [];

    public DefaultAgentContextSession(
        string sessionId,
        string subjectId,
        IContextAssembler assembler,
        IConversationStore conversationStore,
        IMessageTagPipeline tagPipeline)
    {
        SessionId = sessionId;
        SubjectId = subjectId;
        _assembler = assembler;
        _conversationStore = conversationStore;
        _tagPipeline = tagPipeline;
    }

    public string SessionId { get; }
    public string SubjectId { get; }

    public AgentDecisionContext Current =>
        _current ?? throw new InvalidOperationException("Session snapshot has not been created yet.");

    public async Task<AgentDecisionContext> CreateTurnSnapshotAsync(
        ContextBuildRequest request,
        CancellationToken ct = default)
    {
        var built = await _assembler.BuildAsync(request, ct);
        var conversation = (await _conversationStore.GetRecentAsync(SessionId, 32, ct)).ToList();

        lock (_gate)
        {
            _conversation = conversation;
            _snapshotVersion++;
            _current = built with
            {
                SnapshotVersion = _snapshotVersion,
                Conversation = _conversation
            };
        }
        return _current;
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
                Conversation = conversation
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
                SpeakerId = string.IsNullOrWhiteSpace(completion.SenderName) ? SubjectId : completion.SenderName!,
                SpeakerRole = "inbound",
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
                SpeakerRole = "outbound",
                Content = text,
                TimestampUtc = DateTimeOffset.UtcNow
            }, ct));
        }
        return writes.Count == 0 ? Task.CompletedTask : Task.WhenAll(writes);
    }
}
