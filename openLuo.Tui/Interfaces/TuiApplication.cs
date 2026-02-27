using Terminal.Gui;
using openLuo.Capabilities.Core;
using TuiApp = Terminal.Gui.Application;

namespace openLuo.Interfaces.TUI;

public sealed class TuiApplication
{
    private readonly IAgentRuntime _runtime;
    private AgentSession? _session;
    private TextView _history = null!;
    private TextField _input = null!;

    public TuiApplication(IAgentRuntime runtime) => _runtime = runtime;

    public async Task RunAsync(CancellationToken ct = default)
    {
        _session = await _runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = "tui-session", SubjectId = "builtin-rin", AgentId = "companion", ClientType = "tui", ClientId = "local"
        }, ct);
        TuiApp.Init();
        var top = TuiApp.Top;
        _history = new TextView { ReadOnly = true, X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2), WordWrap = true };
        _input = new TextField { X = 0, Y = Pos.Bottom(_history), Width = Dim.Fill() };
        _input.KeyPress += args =>
        {
            if (args.KeyEvent.Key != Key.Enter) return;
            var text = _input.Text?.ToString()?.Trim();
            _input.Text = string.Empty;
            if (!string.IsNullOrWhiteSpace(text)) _ = HandleAsync(text, ct);
            args.Handled = true;
        };
        top.Add(_history, _input);
        _input.SetFocus();
        TuiApp.Run();
        TuiApp.Shutdown();
    }

    private async Task HandleAsync(string text, CancellationToken ct)
    {
        Append($"> {text}\n");
        var result = await _runtime.RunTurnAsync(new TurnRequest
        {
            SessionId = _session!.SessionId, TurnId = Guid.NewGuid().ToString("N"), SourceId = "tui",
            ChannelId = _session.ConversationId, ActorId = "player", Text = text
        }, ct);
        if (!string.IsNullOrWhiteSpace(result.FinalText)) Append(result.FinalText + "\n");
        foreach (var output in result.Outputs) Append($"[{output.Kind}] {output.Payload}\n");
    }

    private void Append(string text) => TuiApp.MainLoop.Invoke(() => _history.Text = (_history.Text?.ToString() ?? string.Empty) + text);
}
