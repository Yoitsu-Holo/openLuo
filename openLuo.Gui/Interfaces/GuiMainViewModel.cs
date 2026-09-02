using System.Collections.ObjectModel;
using openLuo.Capabilities.Core;

namespace openLuo.Interfaces.GUI;

public sealed class GuiMainViewModel
{
    private readonly IAgentRuntime _runtime;
    private AgentSession? _session;
    public ObservableCollection<string> Messages { get; } = [];

    public GuiMainViewModel(IAgentRuntime runtime) => _runtime = runtime;

    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        _session ??= await _runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "gui-session", SubjectId = "builtin-rin", AgentId = "companion", ClientType = "gui", ClientId = "local"
        }, ct);
        Messages.Add($"你: {text}");
        var result = await _runtime.RunTurnAsync(new TurnRequest
        {
            SessionId = _session.SessionId, TurnId = Guid.NewGuid().ToString("N"), SourceId = "gui",
            ChannelId = _session.ConversationId, ActorId = "player", Text = text
        }, ct);
        foreach (var output in result.Outputs) Messages.Add($"[{output.Kind}] {output.Payload}");
        if (!string.IsNullOrWhiteSpace(result.FinalText)) Messages.Add($"角色: {result.FinalText}");
    }
}
