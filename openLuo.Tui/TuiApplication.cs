using System.Text.Json.Nodes;
using Terminal.Gui;
using openLuo.Client;
using openLuo.Protocol;
using TuiApp = Terminal.Gui.Application;

namespace openLuo.Tui;

/// <summary>TUI 客户端：经 Wire 协议连 Hub（**不引用内核**）。</summary>
public sealed class TuiApplication
{
    private readonly string _hubUrl;
    private readonly string _subjectId;
    private readonly string _agentId;
    private readonly string? _conversationId;
    private readonly string? _userId;
    private readonly string? _threadId;

    private HubClient? _client;
    private TextView _history = null!;
    private TextField _input = null!;

    public TuiApplication(string hubUrl, string subjectId = "builtin-rin", string agentId = "companion",
        string? conversationId = null, string? userId = null, string? threadId = null)
    {
        _hubUrl = hubUrl;
        _subjectId = subjectId;
        _agentId = agentId;
        _conversationId = conversationId;
        _userId = userId;
        _threadId = threadId;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _client = await HubClient.ConnectAsync(_hubUrl, $"tui_{ProtocolIds.NewUlid()}", "tui", ct: ct);
        await _client.OpenSessionAsync(_subjectId, _agentId, _conversationId, ct);

        TuiApp.Init();
        var top = TuiApp.Top;

        _history = new TextView { ReadOnly = true, X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2), WordWrap = true };
        _input = new TextField { X = 0, Y = Pos.Bottom(_history), Width = Dim.Fill() };
        _input.KeyPress += args =>
        {
            if (args.KeyEvent.Key != Key.Enter)
                return;

            var text = _input.Text?.ToString()?.Trim();
            _input.Text = string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
                _ = HandleAsync(text, ct);

            args.Handled = true;
        };

        top.Add(_history, _input);
        _input.SetFocus();
        TuiApp.Run();
        TuiApp.Shutdown();

        await _client.DisposeAsync();
        _client = null;
    }

    /// <summary>
    /// 构造回合请求：<c>UserId</c>（说话人稳定身份）与 <c>ThreadId</c>（会话内线程）随每一回合上送；
    /// 为空即不序列化（协议 JSON 忽略 null）= 与从前逐字一致的旧行为。
    /// 单独成方法是为了可测：TUI 的输入循环依赖 Terminal.Gui 初始化（无 TTY 的环境跑不起来），
    /// 把"请求构造"这段纯逻辑抽出来，测试才能断言身份真的贯穿进了 DTO。
    /// </summary>
    internal TurnRequestDto BuildTurnRequest(string text) => new()
    {
        Text = text,
        SourceId = "tui",
        UserId = _userId,
        ThreadId = _threadId,
    };

    private async Task HandleAsync(string text, CancellationToken ct)
    {
        var client = _client;
        if (client is null)
            return;

        Append($"> {text}\n");

        await foreach (var evt in client.StreamTurnAsync(BuildTurnRequest(text), ct))
        {
            switch (evt.Type)
            {
                case EventTypes.Output:
                    if (evt.DataAs<OutputDto>() is { } interim)
                    {
                        Append(Render(interim) + "\n");
                        // 投递回执（任一端 ack 即视为已投递，见协议 §4.6）
                        await client.SendAsync(EnvelopeFactory.Create(
                            MessageTypes.OutputAck, new OutputAckCommand { Sequence = interim.Sequence }), ct);
                    }
                    break;

                case EventTypes.TurnFinal:
                    if (evt.DataAs<TurnResultDto>() is { } result)
                    {
                        foreach (var output in result.Outputs)
                            Append(Render(output) + "\n");
                        if (!string.IsNullOrWhiteSpace(result.FinalText))
                            Append(result.FinalText + "\n");
                    }
                    break;

                case EventTypes.Error:
                    Append($"[error] {evt.ErrorCode} ({ErrorCodes.NameOf(evt.ErrorCode)}): {evt.ErrorMsg}\n");
                    break;
            }
        }
    }

    private static string Render(OutputDto output) => output.Kind switch
    {
        OutputKind.Text => Text(output.Payload),
        OutputKind.Card => $"[card] {output.Payload?.ToJsonString()}",
        _ => output.AssetRef is not null ? $"[{output.Kind}] {output.AssetRef.Id}" : $"[{output.Kind}] {Text(output.Payload)}",
    };

    private static string Text(JsonNode? payload) =>
        payload is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? value.GetValue<string>()
            : payload?.ToJsonString() ?? string.Empty;

    private void Append(string text) =>
        TuiApp.MainLoop.Invoke(() => _history.Text = (_history.Text?.ToString() ?? string.Empty) + text);
}
