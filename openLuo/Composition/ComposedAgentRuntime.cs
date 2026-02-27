using System.Collections.Concurrent;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.AgentContext.Infrastructure;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using openLuo.Infrastructure.Database;

namespace openLuo.Composition;

/// <summary>Default host implementation of the new IAgentRuntime composition surface.</summary>
public sealed class ComposedAgentRuntime : IAgentRuntime
{
    private readonly ICapabilityCatalog _catalog;
    private readonly ICapabilityDecisionLoop _loop;
    private readonly IContextAssembler _assembler;
    private readonly IConversationStore _conversationStore;
    private readonly IMessageTagPipeline _tagPipeline;
    private readonly IOutputQueue _outputQueue;
    private readonly SessionStore _sessions;
    private readonly DatabaseInitializer? _databaseInitializer;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public ComposedAgentRuntime(
        ICapabilityCatalog catalog, ICapabilityDecisionLoop loop, IContextAssembler assembler,
        IConversationStore conversationStore, IMessageTagPipeline tagPipeline, IOutputQueue outputQueue,
        SessionStore sessions, DatabaseInitializer? databaseInitializer = null)
    {
        _catalog = catalog;
        _loop = loop;
        _assembler = assembler;
        _conversationStore = conversationStore;
        _tagPipeline = tagPipeline;
        _outputQueue = outputQueue;
        _sessions = sessions;
        _databaseInitializer = databaseInitializer;
    }

    /// <summary>首次会话前惰性初始化数据库 schema（扩展表：memories/vec_memories 等）。</summary>
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized || _databaseInitializer is null)
            return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;
            await _databaseInitializer.InitializeAsync();
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var conversationId = request.ConversationId ?? request.SessionId;
        _sessions.GetOrAdd(request.SessionId, _ => new DefaultAgentContextSession(request.SessionId, request.SubjectId, _assembler, _conversationStore, _tagPipeline));
        return new AgentSession { SessionId = request.SessionId, SubjectId = request.SubjectId, AgentId = request.AgentId, ConversationId = conversationId };
    }

    public async Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default)
    {
        var session = _sessions.Get(request.SessionId)
            ?? throw new InvalidOperationException($"Session is not open: {request.SessionId}");
        var budgets = request.Budgets ?? DecisionBudgets.Default;
        var built = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = request.SessionId, SubjectId = session.SubjectId, TurnId = request.TurnId,
            UserInput = request.Text, UserBlocks = request.Blocks, ReceivedAtUtc = DateTimeOffset.UtcNow,
            Extras = request.Meta is { Count: > 0 }
                ? request.Meta.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        }, ct);
        var snapshot = await _catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            SessionId = request.SessionId, SubjectId = session.SubjectId, TurnId = request.TurnId, Budgets = budgets
        }, ct);
        var context = AgentContextConverter.ToDecisionContext(built, snapshot, budgets);
        var systemBlocks = built.Contributions.Select(c => $"[{c.Region}]\n{c.Content}\n[/{c.Region}]").ToList();
        var result = await _loop.RunAsync(new DecisionLoopRequest
        {
            SessionId = request.SessionId, TurnId = request.TurnId, SubjectId = session.SubjectId,
            Context = context, Catalog = snapshot, Budgets = budgets,
            BaseExecutionContext = new CapabilityExecutionContext
            {
                GameId = request.SessionId, SessionId = request.SessionId, TurnId = request.TurnId,
                SubjectId = session.SubjectId, SnapshotVersion = built.SnapshotVersion,
                DeadlineUtc = DateTimeOffset.UtcNow + budgets.OverallDeadline, OutputQueue = _outputQueue,
                SystemBlocks = systemBlocks
            }
        }, ct);
        var sanitizedOutputs = result.Outputs
            .Select(o => o.Kind == ReplyItemKind.Text && o.Payload is string text
                ? o with { Payload = AgentContextConverter.SanitizeMetadataPrefix(text) }
                : o)
            .ToList();
        var sanitizedFinalText = AgentContextConverter.SanitizeMetadataPrefix(result.FinalText);
        await session.CommitTurnAsync(new TurnCompletion
        {
            TurnId = request.TurnId, FinalText = sanitizedFinalText, Outputs = sanitizedOutputs, Success = result.Success,
            UserText = request.Text, SenderName = request.SenderName
        }, ct);
        return new TurnResult
        {
            Success = result.Success, FinalText = sanitizedFinalText, Outputs = sanitizedOutputs,
            Steps = result.Steps, TerminationReason = result.TerminationReason,
            TerminationDetail = result.TerminationDetail, StateVersion = built.SnapshotVersion
        };
    }

    public async Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default)
    {
        var session = _sessions.Get(sessionId);
        if (session is null)
            return null;
        AgentDecisionContext? current;
        try
        {
            current = session.Current;
        }
        catch (InvalidOperationException)
        {
            return $"session: {sessionId} (opened, no turn yet)";
        }
        var history = await _conversationStore.GetRecentAsync(sessionId, 32, ct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"session: {sessionId}  subject: {session.SubjectId}  snapshot: v{current.SnapshotVersion}");
        sb.AppendLine($"contributions: {current.Contributions.Count}");
        foreach (var c in current.Contributions)
            sb.AppendLine($"  [{c.Region}] {c.Content}");
        sb.AppendLine($"conversation: {history.Count} turns");
        foreach (var t in history.TakeLast(8))
            sb.AppendLine($"  {t.SpeakerRole}({t.SpeakerId}): {(t.Content.Length > 80 ? t.Content[..80] + "…" : t.Content)}");
        sb.AppendLine($"userInput: {current.UserInput}");
        return sb.ToString();
    }

    public async IAsyncEnumerable<TurnEvent> StreamTurnAsync(TurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await RunTurnAsync(request, ct);
        foreach (var output in result.Outputs)
            yield return new TurnEvent { TurnId = request.TurnId, Kind = "output", Payload = output };
        yield return new TurnEvent { TurnId = request.TurnId, Kind = "final", Payload = result };
    }
}

/// <summary>AgentDecisionContext ↔ CapabilityDecisionContext 转换。</summary>
public static partial class AgentContextConverter
{
    public static CapabilityDecisionContext ToDecisionContext(
        AgentDecisionContext built,
        CapabilityCatalogSnapshot? catalog,
        DecisionBudgets budgets) => new()
    {
        SessionId = built.SessionId, TurnId = built.TurnId, SnapshotVersion = built.SnapshotVersion,
        SystemBlocks = built.Contributions.Select(c => $"[{c.Region}]\n{c.Content}\n[/{c.Region}]").ToList(),
        Conversation = built.Conversation.Select(t => t.SpeakerRole == "tool"
            ? new ContextMessage { Role = "tool", Content = t.Content, ToolCallId = t.ToolCallId }
            : new ContextMessage
            {
                Role = t.SpeakerRole is "outbound" or "assistant" ? "assistant" : "user",
                Content = FormatChatLine(t),
                ToolCallsJson = t.ToolCallsJson
            }).ToList(),
        UserInput = built.UserInput, Budgets = budgets,
        Capabilities = catalog is null
            ? built.Capabilities
            : catalog.ByCanonicalId.Values.Select(d => new CapabilitySummary
            {
                CanonicalId = d.CanonicalId, ModelToolName = d.ModelToolName, DisplayName = d.DisplayName, Summary = d.Summary, Usage = d.Usage, InputSchema = d.InputSchema
            }).ToList(),
        Skills = built.Skills, Workflows = built.Workflows, RemoteAgents = built.RemoteAgents
    };

    /// <summary>
    /// 对话消息统一元信息（D29 注入面收缩）：
    /// assistant 消息为纯内容（消除元信息前缀模仿模板）；
    /// user 消息仅标注 [FROM: 说话人]；逐条 [TIME:] 由 TimeContext 增强块承担；
    /// [TYPE:] 仅非 text 消息标注（多模态）。
    /// </summary>
    private static string FormatChatLine(ConversationTurn turn)
    {
        if (turn.SpeakerRole is "outbound" or "assistant")
            return SanitizeMetadataPrefix(turn.Content);

        var type = turn.Metadata.TryGetValue("type", out var t) ? t : string.Empty;
        var typePrefix = string.IsNullOrWhiteSpace(type) || type.Equals("text", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"[TYPE: {type}] ";
        var sender = string.IsNullOrWhiteSpace(turn.SpeakerId) ? "user" : turn.SpeakerId;
        return $"{typePrefix}[FROM: {sender}] {turn.Content}";
    }

    /// <summary>
    /// 输出净化兜底：剥离模型模仿的元信息前缀（容错残缺变体）与 XML/DSML 工具调用块，
    /// 保证最终回复与历史中不出现 [TIME:]/[TYPE:]/[FROM:] 前缀或裸 &lt;tool_calls&gt;/&lt;invoke&gt; 文本。
    /// </summary>
    public static string SanitizeMetadataPrefix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var current = text;
        for (var i = 0; i < 3; i++)
        {
            var stripped = XmlToolCallRegex().Replace(current, string.Empty);
            if (stripped.Length == current.Length)
                break;
            current = stripped;
        }
        for (var i = 0; i < 3; i++)
        {
            var stripped = MetadataPrefixRegex().Replace(current, string.Empty, count: 1);
            if (ReferenceEquals(stripped, current) || stripped.Length == current.Length)
                break;
            current = stripped;
        }
        return current.Trim();
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"<[^>]*tool_calls[^>]*>.*?</[^>]*tool_calls[^>]*>|<[^>]*invoke[^>]*>.*?</[^>]*invoke[^>]*>|</?[^>]*(?:tool_calls|invoke)[^>]*>|｜｜\w+",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex XmlToolCallRegex();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^[\s\u3000]*(?:\[(?:TIME:\s*)?\d{4}/\d{2}/\d{2}\s+\d{2}:\d{2}:\d{2}\]\s*)?(?:\[TYPE:\s*[a-z]+\]\s*)?(?:\[FROM:\s*[^\]]+\]\s*)?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex MetadataPrefixRegex();
}

/// <summary>会话感知的上下文更新器：工具结果应用到会话快照并重建完整决策上下文。</summary>
public sealed class SessionContextUpdater : IContextUpdater
{
    private readonly SessionStore _sessions;
    private readonly ICapabilityCatalog _catalog;
    public SessionContextUpdater(SessionStore sessions, ICapabilityCatalog catalog)
    {
        _sessions = sessions;
        _catalog = catalog;
    }

    public async Task<CapabilityDecisionContext> ApplyToolResultsAsync(
        string sessionId,
        string turnId,
        IReadOnlyList<CapabilityCall> calls,
        IReadOnlyList<CapabilityResult> results,
        CancellationToken ct = default)
    {
        var session = _sessions.Get(sessionId)
            ?? throw new InvalidOperationException($"Session is not open: {sessionId}");
        var built = await session.ApplyToolResultsAsync(calls, results, ct);
        // 工具结果轮重建能力快照：让决策模型保留原生 tools（MCP 工具固定，
        // 但快照必须携带 capabilities，否则 tools=0 会迫使模型用文本模拟调用）。
        var snapshot = await _catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            SessionId = sessionId, SubjectId = session.SubjectId, TurnId = turnId, Budgets = built.Budgets
        }, ct);
        return AgentContextConverter.ToDecisionContext(built, snapshot, built.Budgets);
    }
}
