using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Capabilities.Infrastructure;
using openLuo.Capabilities.Llm;
using openLuo.Composition;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;

var demo = PlaygroundComposition.Create();
var session = await demo.Runtime.OpenSessionAsync(new SessionOpenRequest
{
    SessionId = "playground-session", SubjectId = "player", AgentId = "demo", ClientType = "console", ClientId = "local"
});

Console.WriteLine("openLuo Playground — native capability decision loop");
Console.WriteLine("Type a message, or 'quit'.");
while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null || line.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
    if (string.IsNullOrWhiteSpace(line)) continue;
    var result = await demo.Runtime.RunTurnAsync(new TurnRequest
    {
        SessionId = session.SessionId, TurnId = Guid.NewGuid().ToString("N"),
        SourceId = "playground", ChannelId = session.ConversationId, ActorId = "player", Text = line
    });
    Console.WriteLine(result.FinalText ?? $"[{result.TerminationReason}] no final text");
}

internal sealed class PlaygroundComposition
{
    public required IAgentRuntime Runtime { get; init; }

    public static PlaygroundComposition Create()
    {
        var catalog = new DefaultCapabilityCatalog([new PlaygroundCapabilitySource()]);
        catalog.LoadBase();
        var queue = new InMemoryOutputQueue();
        var dispatcher = new DefaultCapabilityDispatcher(new PlaygroundInvoker(), new DefaultCapabilityPolicy(), new InMemoryStateTransaction());
        var loop = new DefaultCapabilityDecisionLoop(
            new LlmCapabilityDecisionModel(new PlaygroundLlmClient()), dispatcher,
            new PlaygroundContextUpdater(), new SystemClock());
        var assembler = new openLuo.AgentContext.Infrastructure.DefaultContextAssembler([]);
        var store = new PlaygroundConversationStore();
        var tags = new openLuo.AgentContext.Infrastructure.DefaultMessageTagPipeline();
        return new PlaygroundComposition { Runtime = new ComposedAgentRuntime(catalog, loop, assembler, store, tags, queue, new SessionStore()) };
    }
}

internal sealed class PlaygroundCapabilitySource : ICapabilitySource
{
    public string ProviderId => "playground";
    public IReadOnlyList<CapabilityDescriptor> ListCapabilities() => [new CapabilityDescriptor
    {
        CanonicalId = "playground:echo", Kind = CapabilityKind.Builtin, ProviderId = ProviderId,
        DisplayName = "Echo", Summary = "Echo the supplied text.", Usage = "Use to demonstrate native tool calls."
    }];
}

internal sealed class PlaygroundInvoker : ICapabilityInvoker
{
    public Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default) =>
        Task.FromResult(new CapabilityResult { InvocationId = call.InvocationId, Success = true, Status = CapabilityStatus.Ok, Text = $"echo: {string.Join(' ', call.Args)}" });
}

internal sealed class PlaygroundContextUpdater : IContextUpdater
{
    public Task<CapabilityDecisionContext> ApplyToolResultsAsync(string sessionId, string turnId, IReadOnlyList<CapabilityCall> calls, IReadOnlyList<CapabilityResult> results, CancellationToken ct = default) =>
        Task.FromResult(new CapabilityDecisionContext { SessionId = sessionId, TurnId = turnId, SystemBlocks = results.Select(r => r.Text ?? r.Error ?? string.Empty).ToList() });
}

internal sealed class PlaygroundConversationStore : IConversationStore
{
    private readonly List<ConversationTurn> _turns = [];
    public Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(string sessionId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConversationTurn>>(_turns.Where(t => t.SessionId == sessionId).TakeLast(limit).ToList());
    public Task AppendAsync(ConversationTurn turn, CancellationToken ct = default) { _turns.Add(turn); return Task.CompletedTask; }
}

internal sealed class PlaygroundLlmClient : ILlmClient
{
    public Task<LlmChatResponse> CompleteAsync(IEnumerable<ChatMessage> messages, LlmOptions? options = null, CancellationToken ct = default) =>
        Task.FromResult(new LlmChatResponse { Content = messages.LastOrDefault()?.Content is { } text ? $"Playground reply: {text}" : "Playground ready" });
    public Task<string> StreamAsync(IEnumerable<ChatMessage> messages, Action<string> onChunk, LlmOptions? options = null, CancellationToken ct = default) =>
        Task.FromResult("Playground ready");
}
