using openLuo.Client;
using openLuo.Protocol;

namespace openLuo.Cli;

/// <summary>
/// CLI 客户端：经 Wire 协议连 Hub（**不引用内核**，Hub 为唯一权威）。
/// 输入解析在客户端；回合执行与能力/记忆/状态均在 Hub。
/// </summary>
public sealed class CliHubApp
{
    private readonly string _hubUrl;
    private readonly string _subjectId;
    private readonly string _agentId;
    private readonly Func<string, Task> _writeLine;

    public CliHubApp(string hubUrl, string subjectId = "builtin-rin", string agentId = "companion", Func<string, Task>? writeLine = null)
    {
        _hubUrl = hubUrl;
        _subjectId = subjectId;
        _agentId = agentId;
        _writeLine = writeLine ?? (text => { Console.WriteLine(text); return Task.CompletedTask; });
    }

    public async Task RunAsync(TextReader input, CancellationToken ct = default)
    {
        await using var client = await HubClient.ConnectAsync(_hubUrl, $"cli_{ProtocolIds.NewUlid()}", "cli", ct: ct);
        var session = await client.OpenSessionAsync(_subjectId, _agentId, ct: ct);
        await _writeLine($"[connected] protocol=v{client.Welcome?.ProtocolVersion} session={session.SessionId}");

        while (!ct.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(ct);
            if (line is null)
                return;

            var parsed = CliInputParser.Parse(line);
            if (parsed.Kind == CliInputKind.Empty)
                continue;

            if (parsed.Kind == CliInputKind.Text && parsed.Text.Equals("quit", StringComparison.OrdinalIgnoreCase))
                return;

            if (parsed.Kind == CliInputKind.Command)
            {
                if (parsed.Command.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    return;
                await _writeLine($"command: /{parsed.Command}");
                continue;
            }

            await RunTurnAsync(client, parsed.Text, ct);
        }
    }

    private async Task RunTurnAsync(HubClient client, string text, CancellationToken ct)
    {
        await foreach (var evt in client.StreamTurnAsync(new TurnRequestDto { Text = text, SourceId = "cli" }, ct))
        {
            switch (evt.Type)
            {
                case EventTypes.Output:
                    // 回合进行中的即发输出（interim / 音频）
                    if (evt.DataAs<OutputDto>() is { } interim)
                        await _writeLine(CliRenderer.Render(interim));
                    break;

                case EventTypes.TurnFinal:
                {
                    if (evt.DataAs<TurnResultDto>() is not { } result)
                        break;

                    foreach (var output in result.Outputs)
                        await _writeLine(CliRenderer.Render(output));

                    if (!string.IsNullOrWhiteSpace(result.FinalText))
                        await _writeLine(result.FinalText);
                    else if (result.Outputs.Count == 0)
                        await _writeLine($"[terminated: {result.TerminationReason}]");
                    break;
                }

                case EventTypes.Error:
                    await _writeLine($"[error] {evt.ErrorCode} ({ErrorCodes.NameOf(evt.ErrorCode)}): {evt.ErrorMsg}");
                    break;
            }
        }
    }
}
