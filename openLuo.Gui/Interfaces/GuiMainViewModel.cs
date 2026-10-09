using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using openLuo.Client;
using openLuo.Protocol;

namespace openLuo.Interfaces.GUI;

/// <summary>GUI 视图模型：经 Wire 协议连 Hub（**不引用内核**）。</summary>
public sealed class GuiMainViewModel
{
    private readonly string _hubUrl;
    private readonly string _subjectId;
    private readonly string _agentId;

    private HubClient? _client;

    public ObservableCollection<string> Messages { get; } = [];

    public GuiMainViewModel(string hubUrl, string subjectId = "builtin-rin", string agentId = "companion")
    {
        _hubUrl = hubUrl;
        _subjectId = subjectId;
        _agentId = agentId;
    }

    private async Task<HubClient> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is not null)
            return _client;

        _client = await HubClient.ConnectAsync(_hubUrl, $"gui_{ProtocolIds.NewUlid()}", "gui", ct: ct);
        await _client.OpenSessionAsync(_subjectId, _agentId, ct: ct);
        return _client;
    }

    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        var client = await EnsureConnectedAsync(ct);
        Messages.Add($"你: {text}");

        await foreach (var evt in client.StreamTurnAsync(new TurnRequestDto { Text = text, SourceId = "gui" }, ct))
        {
            switch (evt.Type)
            {
                case EventTypes.Output:
                    if (evt.DataAs<OutputDto>() is { } interim)
                        Messages.Add(Render(interim));
                    break;

                case EventTypes.TurnFinal:
                    if (evt.DataAs<TurnResultDto>() is { } result)
                    {
                        foreach (var output in result.Outputs)
                            Messages.Add(Render(output));
                        if (!string.IsNullOrWhiteSpace(result.FinalText))
                            Messages.Add($"角色: {result.FinalText}");
                    }
                    break;

                case EventTypes.Error:
                    Messages.Add($"[error] {evt.ErrorCode} ({ErrorCodes.NameOf(evt.ErrorCode)}): {evt.ErrorMsg}");
                    break;
            }
        }
    }

    private static string Render(OutputDto output) => output.Kind switch
    {
        OutputKind.Card => $"[card] {output.Payload?.ToJsonString()}",
        _ => output.AssetRef is not null ? $"[{output.Kind}] {output.AssetRef.Id}" : $"[{output.Kind}] {Text(output.Payload)}",
    };

    private static string Text(JsonNode? payload) =>
        payload is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? value.GetValue<string>()
            : payload?.ToJsonString() ?? string.Empty;
}
