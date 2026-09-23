using openLuo.Capabilities.Core;

namespace openLuo.Cli;

/// <summary>Minimal CLI host: input parsing is platform-owned; turn execution is runtime-owned.</summary>
public sealed class CliApplication
{
    private readonly IAgentRuntime _runtime;
    private readonly Func<string, Task> _writeLine;

    public CliApplication(IAgentRuntime runtime, Func<string, Task>? writeLine = null)
    {
        _runtime = runtime;
        _writeLine = writeLine ?? (text => { Console.WriteLine(text); return Task.CompletedTask; });
    }

    public async Task RunAsync(AgentSession session, TextReader input, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(ct);
            if (line is null)
                return;
            var parsed = CliInputParser.Parse(line);
            if (parsed.Kind == CliInputKind.Empty)
                continue;
            if (parsed.Kind == CliInputKind.Command && parsed.Command.Equals("quit", StringComparison.OrdinalIgnoreCase))
                return;
            if (parsed.Kind == CliInputKind.Text && parsed.Text.Equals("quit", StringComparison.OrdinalIgnoreCase))
                return;
            if (parsed.Kind == CliInputKind.Command)
            {
                await _writeLine($"command: /{parsed.Command}");
                continue;
            }

            var result = await _runtime.RunTurnAsync(new TurnRequest
            {
                SessionId = session.SessionId,
                TurnId = Guid.NewGuid().ToString("N"),
                SourceId = "cli",
                ChannelId = session.ConversationId,
                ActorId = "player",
                Text = parsed.Text
            }, ct);
            foreach (var output in result.Outputs)
                await _writeLine(CliRenderer.Render(output));
            if (!string.IsNullOrWhiteSpace(result.FinalText))
            {
                await _writeLine(result.FinalText);
            }
            else if (result.Outputs.Count == 0 && result.TerminationReason != TerminationReason.FinalReply)
            {
                await _writeLine($"[terminated: {result.TerminationReason}{(string.IsNullOrWhiteSpace(result.TerminationDetail) ? string.Empty : $": {result.TerminationDetail}")}]");
            }
        }
    }
}
