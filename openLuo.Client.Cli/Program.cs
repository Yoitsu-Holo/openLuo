using openLuo.Client;
using openLuo.Protocol;

var url = Arg("--url") ?? $"ws://127.0.0.1:{ProtocolInfo.DefaultPort}{ProtocolInfo.StreamPath}";
var subject = Arg("--subject") ?? "builtin-rin";
var agent = Arg("--agent") ?? "companion";

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

await using var client = await HubClient.ConnectAsync(url, $"cli_{ProtocolIds.NewUlid()}", "cli");
Console.WriteLine($"[connected] protocol=v{client.Welcome?.ProtocolVersion} server={client.Welcome?.ServerVersion}");

var session = await client.OpenSessionAsync(subject, agent);
Console.WriteLine($"[session] {session.SessionId}");

string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line))
        continue;
    if (line is "/quit" or "/exit")
        break;

    await foreach (var evt in client.StreamTurnAsync(new TurnRequestDto { Text = line, SourceId = "cli" }))
    {
        switch (evt.Type)
        {
            case EventTypes.Decision:
                Console.WriteLine($"[decision] step={evt.DataAs<DecisionStepEvent>()?.Step}");
                break;
            case EventTypes.ToolCall:
                Console.WriteLine($"[tool.call] {evt.DataAs<ToolCallEvent>()?.CanonicalId}");
                break;
            case EventTypes.ToolResult:
            {
                var r = evt.DataAs<ToolResultEvent>();
                Console.WriteLine($"[tool.result] {r?.CallId} {r?.Status}");
                break;
            }
            case EventTypes.Output:
            {
                var o = evt.DataAs<OutputDto>();
                Console.WriteLine($"[output] {o?.Kind}: {o?.Payload}");
                break;
            }
            case EventTypes.TurnFinal:
                Console.WriteLine($"[final] {evt.DataAs<TurnResultDto>()?.FinalText}");
                break;
            case EventTypes.Error:
                Console.WriteLine($"[error] {evt.ErrorCode} ({ErrorCodes.NameOf(evt.ErrorCode)}): {evt.ErrorMsg}");
                break;
        }
    }
}

Console.WriteLine("[bye]");
