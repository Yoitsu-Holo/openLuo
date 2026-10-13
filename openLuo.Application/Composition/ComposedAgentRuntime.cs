using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
    private readonly ITokenCounter _tokenCounter;
    private readonly IContextLimitsProvider _contextLimits;
    private readonly openLuo.Core.Interfaces.IGameLogger? _logger;
    private readonly ITurnCatalog _turnCatalog;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    /// <summary>
    /// 每会话回合并发闸：同一会话的回合（含流式）串行执行。
    /// 会话快照推进（CreateTurnSnapshot → ApplyToolResults → Commit）不是一个原子事务；
    /// 若同会话两个回合交错，会基于同一基础快照互相覆盖（逻辑竞态，非内存损坏）。
    /// 平台层（如 QQ fire-and-forget）可能并发投递，故在此收口。会话关闭后条目不回收，量级可控。
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _turnGates = new(StringComparer.Ordinal);

    private bool _initialized;

    public ComposedAgentRuntime(
        ICapabilityCatalog catalog, ICapabilityDecisionLoop loop, IContextAssembler assembler,
        IConversationStore conversationStore, IMessageTagPipeline tagPipeline, IOutputQueue outputQueue,
        SessionStore sessions, DatabaseInitializer? databaseInitializer = null,
        ITokenCounter? tokenCounter = null, IContextLimitsProvider? contextLimits = null,
        openLuo.Core.Interfaces.IGameLogger? logger = null, ITurnCatalog? turnCatalog = null)
    {
        _catalog = catalog;
        _loop = loop;
        _assembler = assembler;
        _conversationStore = conversationStore;
        _tagPipeline = tagPipeline;
        _outputQueue = outputQueue;
        _sessions = sessions;
        _databaseInitializer = databaseInitializer;
        _tokenCounter = tokenCounter ?? HeuristicTokenCounter.Instance;
        _contextLimits = contextLimits ?? StaticContextLimitsProvider.Default;
        _logger = logger;
        _turnCatalog = turnCatalog ?? NullTurnCatalog.Instance;
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

        // 会话寻址（"多端共享同一段对话"的支点）：给了 conversationId 就按 (subjectId, agentId, conversationId)
        // 复用既有会话——PC 客户端与手机 QQ 只要配置同一 conversationId 就落在同一段对话历史里。
        // 未给 conversationId 时行为不变：每次新建（其 conversationId 缺省等于本次 sessionId）。
        if (!string.IsNullOrWhiteSpace(request.ConversationId) &&
            _sessions.FindByConversation(request.SubjectId, request.AgentId, request.ConversationId) is { } existing)
        {
            _sessions.SetMeta(existing, request.ClientType, request.ClientId);   // 刷新"最近活跃客户端"
            return existing;
        }

        var conversationId = request.ConversationId ?? request.SessionId;
        _sessions.GetOrAdd(request.SessionId, _ => new DefaultAgentContextSession(
            request.SessionId, request.SubjectId, _assembler, _conversationStore, _tagPipeline,
            _tokenCounter, _contextLimits, conversationId));
        var session = new AgentSession { SessionId = request.SessionId, SubjectId = request.SubjectId, AgentId = request.AgentId, ConversationId = conversationId };
        _sessions.SetMeta(session, request.ClientType, request.ClientId);
        return session;
    }

    public Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult(_sessions.GetMeta(sessionId));

    public Task<IReadOnlyList<AgentSession>> ListSessionsAsync(CancellationToken ct = default) =>
        Task.FromResult(_sessions.ListMeta());

    public Task<bool> CloseSessionAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult(_sessions.Remove(sessionId));

    /// <summary>
    /// 未由客户端覆盖时的回合预算：回合总时限取 <c>agent.chatRoundTimeoutSeconds</c>
    /// （经静态配置门面读当前快照 → 热加载生效）。其余字段用内核默认。
    /// </summary>
    private static DecisionBudgets BuildDefaultBudgets()
    {
        var seconds = openLuo.Infrastructure.Config.Config.Agent.ChatRoundTimeoutSeconds;
        return new DecisionBudgets { OverallDeadline = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
    }

    /// <summary>
    /// 记录本轮上下文预算账目。**这是"被裁"唯一的外部信号**——没有它，预算压力只会表现为
    /// 模型"忽然不知道平台规则/不会用工具"，排查时无从下手。
    /// 被裁条目只记数量与前几条 id（列表可能很长），完整明细在快照 <c>Budget.DroppedContributions</c>。
    /// </summary>
    private void LogContextBudget(string turnId, ContextBudgetSnapshot budget, ContextCacheStats cache,
        int skills = 0, int workflows = 0)
    {
        if (_logger is null)
            return;

        var dropped = budget.DroppedContributions.Count == 0
            ? "-"
            : $"{budget.DroppedContributions.Count}({string.Join(",", budget.DroppedContributions.Take(5).Select(d => d.Id))})";
        _logger.Info("agent/context",
            $"[ctx] turn={turnId} tokens={budget.TotalTokens}/{budget.MaxTokens} " +
            $"system={budget.SystemTokens} conversation={budget.ConversationTokens} " +
            $"(turns dropped={budget.ConversationTurnsDropped}) dropped={dropped} overBudget={budget.OverBudget} " +
            $"cache={cache} skills={skills} workflows={workflows}");
    }

    /// <summary>回合准备结果（快照/目录/决策上下文/请求），供非流式与流式共用。</summary>
    private sealed record PreparedTurn(
        DefaultAgentContextSession Session,
        AgentDecisionContext Built,
        CapabilityCatalogSnapshot Snapshot,
        DecisionLoopRequest LoopRequest);

    private async Task<PreparedTurn> PrepareTurnAsync(TurnRequest request, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        var session = _sessions.Get(request.SessionId)
            ?? throw new InvalidOperationException($"Session is not open: {request.SessionId}");
        var budgets = request.Budgets ?? BuildDefaultBudgets();
        var built = await session.CreateTurnSnapshotAsync(new ContextBuildRequest
        {
            SessionId = request.SessionId, SubjectId = session.SubjectId, TurnId = request.TurnId,
            ConversationId = session.ConversationId,
            UserId = request.UserId, ThreadId = request.ThreadId, Mentions = request.Mentions,
            UserInput = request.Text, UserBlocks = request.Blocks, ReceivedAtUtc = DateTimeOffset.UtcNow,
            Extras = request.Meta is { Count: > 0 }
                ? request.Meta.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        }, ct);
        // 「我能做什么」目录（D30）：摘要随快照注入；正文/编排由 core:load_skill / core:run_workflow 按需拉取。
        built = built with
        {
            Skills = _turnCatalog.Skills,
            Workflows = _turnCatalog.Workflows,
            RemoteAgents = _turnCatalog.RemoteAgents,
        };
        LogContextBudget(request.TurnId, built.Budget, built.Cache, built.Skills.Count, built.Workflows.Count);
        var snapshot = await _catalog.BuildSnapshotAsync(new CatalogBuildContext
        {
            SessionId = request.SessionId, SubjectId = session.SubjectId, TurnId = request.TurnId, Budgets = budgets
        }, ct);
        var context = AgentContextConverter.ToDecisionContext(built, snapshot, budgets);
        var systemBlocks = built.Contributions.Select(c => $"[{c.Region}]\n{c.Content}\n[/{c.Region}]").ToList();
        var loopRequest = new DecisionLoopRequest
        {
            SessionId = request.SessionId, TurnId = request.TurnId, SubjectId = session.SubjectId,
            Context = context, Catalog = snapshot, Budgets = budgets,
            BaseExecutionContext = new CapabilityExecutionContext
            {
                GameId = request.SessionId, SessionId = request.SessionId, TurnId = request.TurnId,
                SubjectId = session.SubjectId, SnapshotVersion = built.SnapshotVersion,
                // 身份与线程随执行上下文下传：能力写入方（如 memory:write）据此把内容落到正确的域（D45）
                UserId = request.UserId, ThreadId = request.ThreadId, ConversationId = session.ConversationId,
                DeadlineUtc = DateTimeOffset.UtcNow + budgets.OverallDeadline, OutputQueue = _outputQueue,
                SystemBlocks = systemBlocks, UserBlocks = request.Blocks
            }
        };
        return new PreparedTurn(session, built, snapshot, loopRequest);
    }

    /// <summary>净化输出并提交回合，产出对外 TurnResult。</summary>
    private async Task<TurnResult> CommitTurnAsync(
        TurnRequest request, PreparedTurn prepared, DecisionLoopResult result, CancellationToken ct)
    {
        var sanitizedOutputs = result.Outputs
            .Select(o => o.Kind == ReplyItemKind.Text && o.Payload is string text
                ? o with { Payload = AgentContextConverter.SanitizeMetadataPrefix(text) }
                : o)
            .ToList();
        var sanitizedFinalText = AgentContextConverter.SanitizeMetadataPrefix(result.FinalText);
        await prepared.Session.CommitTurnAsync(new TurnCompletion
        {
            TurnId = request.TurnId, FinalText = sanitizedFinalText, Outputs = sanitizedOutputs, Success = result.Success,
            UserText = request.Text, SenderName = request.SenderName,
            UserId = request.UserId, ThreadId = request.ThreadId
        }, ct);
        return new TurnResult
        {
            Success = result.Success, FinalText = sanitizedFinalText, Outputs = sanitizedOutputs,
            Steps = result.Steps, TerminationReason = result.TerminationReason,
            TerminationDetail = result.TerminationDetail, StateVersion = prepared.Built.SnapshotVersion
        };
    }

    private SemaphoreSlim TurnGate(string sessionId) =>
        _turnGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));

    public async Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default)
    {
        var gate = TurnGate(request.SessionId);
        await gate.WaitAsync(ct);
        try
        {
            var prepared = await PrepareTurnAsync(request, ct);
            var result = await _loop.RunAsync(prepared.LoopRequest, ct);
            return await CommitTurnAsync(request, prepared, result, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendMessageAsync(string sessionId, string? senderName, string text,
        IReadOnlyList<object>? blocks = null, string? userId = null, string? threadId = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var session = _sessions.Get(sessionId)
            ?? throw new InvalidOperationException($"Session is not open: {sessionId}");
        // 与 CommitTurnAsync 的 inbound 写入对齐：只落库，不触碰内存快照；
        // 下次 turn 的 CreateTurnSnapshotAsync 会重新读取历史，感知消息自然进入上下文。
        await _conversationStore.AppendAsync(new ConversationTurn
        {
            SessionId = sessionId,
            TurnId = $"{Guid.NewGuid():N}:observe",
            // 稳定身份优先（D45）；未提供才回退会话主体。显示名单独保留，仅用于渲染。
            SpeakerId = string.IsNullOrWhiteSpace(userId) ? session.SubjectId : userId!,
            SpeakerName = senderName ?? string.Empty,
            SpeakerRole = "inbound",
            ThreadId = threadId,
            Content = text,
            Blocks = blocks,
            TimestampUtc = DateTimeOffset.UtcNow
        }, ct);
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
        var history = await _conversationStore.GetRecentAsync(sessionId, _contextLimits.Current.ConversationRetainCount, ct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"session: {sessionId}  subject: {session.SubjectId}  snapshot: v{current.SnapshotVersion}");
        sb.AppendLine($"contributions: {current.Contributions.Count}");
        foreach (var c in current.Contributions)
            sb.AppendLine($"  [{c.Region}] {c.Content}");
        sb.AppendLine($"conversation: {history.Count} turns");
        foreach (var t in history.TakeLast(8))
        {
            // 调试摘要同时给出稳定身份与显示名（群聊里两者都有用）：`inbound(10001/阿明): …`
            var who = string.IsNullOrWhiteSpace(t.SpeakerName) || t.SpeakerName == t.SpeakerId
                ? t.SpeakerId
                : $"{t.SpeakerId}/{t.SpeakerName}";
            sb.AppendLine($"  {t.SpeakerRole}({who}): {(t.Content.Length > 80 ? t.Content[..80] + "…" : t.Content)}");
        }
        sb.AppendLine($"userInput: {current.UserInput}");
        return sb.ToString();
    }

    /// <summary>
    /// 真流式回合：准备 → 消费决策循环事件（decision/tool_call/tool_result/output）→ 提交 → final。
    /// 与 <see cref="RunTurnAsync"/> 共用准备与提交逻辑，并以同一会话闸串行化。
    /// </summary>
    public async IAsyncEnumerable<TurnEvent> StreamTurnAsync(
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var gate = TurnGate(request.SessionId);
        await gate.WaitAsync(ct);
        try
        {
            var prepared = await PrepareTurnAsync(request, ct);
            DecisionLoopResult? final = null;

            await foreach (var evt in _loop.RunStreamAsync(prepared.LoopRequest, ct))
            {
                switch (evt.Kind)
                {
                    case DecisionEventKind.Final:
                        final = evt.Payload as DecisionLoopResult;
                        break;
                    case DecisionEventKind.Decision:
                        yield return new TurnEvent { TurnId = request.TurnId, Kind = "decision", Payload = evt.Payload };
                        break;
                    case DecisionEventKind.ToolCall:
                        yield return new TurnEvent { TurnId = request.TurnId, Kind = "tool_call", Payload = evt.Payload };
                        break;
                    case DecisionEventKind.ToolResult:
                        yield return new TurnEvent { TurnId = request.TurnId, Kind = "tool_result", Payload = evt.Payload };
                        break;
                    case DecisionEventKind.Output:
                        yield return new TurnEvent { TurnId = request.TurnId, Kind = "output", Payload = evt.Payload };
                        break;
                }
            }

            if (final is null)
                yield break;

            var result = await CommitTurnAsync(request, prepared, final, ct);
            yield return new TurnEvent { TurnId = request.TurnId, Kind = "final", Payload = result };
        }
        finally
        {
            gate.Release();
        }
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
                Blocks = t.Blocks,
                ToolCallsJson = t.ToolCallsJson
            }).ToList(),
        UserInput = built.UserInput, UserBlocks = built.UserBlocks, Budgets = budgets,
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
        // 渲染给模型看的是**显示名**（昵称/名字），不是稳定 id——模型不需要看到 QQ 号。
        // 稳定 id 只用于归因/键控（D45）；显示名为空时才回退到 id，最后回退到 "user"。
        var sender = turn.SpeakerName;
        if (string.IsNullOrWhiteSpace(sender))
            sender = turn.SpeakerId;
        if (string.IsNullOrWhiteSpace(sender))
            sender = "user";
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
