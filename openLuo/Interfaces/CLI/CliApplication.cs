using openLuo.Core.Models;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;

namespace openLuo.Interfaces.CLI;

public sealed class CliApplication
{
    private readonly IGameSessionCatalog _sessionCatalog;
    private readonly IGameSession _session;
    private GameState? _state;

    public CliApplication(
        IGameSessionCatalog sessionCatalog,
        IGameSession session,
        GameState? state)
    {
        _sessionCatalog = sessionCatalog;
        _session = session;
        _state = state;
    }

    public async Task RunAsync()
    {
        if (_state is null)
        {
            var initialized = await InitializeGameAsync();
            if (!initialized)
            {
                return;
            }
        }
        else
        {
            Console.WriteLine($"\n欢迎回来，{_state.PlayerName}！第 {_state.CurrentDay} 天。\n");
        }

        // 注意：Console.ReadLine() 对中文字符的行内编辑支持有限
        // 但输入的实际内容是正确的，建议使用 Ctrl+U 清空重新输入
        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (input is null
                || input.Equals("/quit", StringComparison.OrdinalIgnoreCase)
                || input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("再见！");
                break;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;

            var result = await _session.SubmitAsync(new GameSessionInput
            {
                SourceId = "cli",
                ChannelId = "main",
                ActorId = "player",
                Kind = SessionInputKind.Text,
                Text = input
            });

            var hasMessageOutput = result.Events.Any(static e => e is MessageEvent);
            foreach (var @event in result.Events)
            {
                if (@event is InputAcceptedEvent accepted)
                    Console.WriteLine($"[输入] {accepted.RawInput} attachments={accepted.Attachments.Count}");
                else if (@event is StatusSnapshotEvent snapshot)
                    Console.WriteLine($"[状态] day={snapshot.CurrentDay?.ToString() ?? "-"} minute={snapshot.CurrentMinute?.ToString() ?? "-"} active={snapshot.ActiveCharacterId ?? "-"}");
                else if (@event is AgentStepEvent step)
                    Console.WriteLine($"[{StepLabel(step.NodeKind)}] {step.CallName}");
                else if (@event is MessageEvent message)
                    PrintMessageOutput(message);
                else if (@event is TextOutputEvent text)
                {
                    if (!hasMessageOutput)
                        Console.WriteLine(text.Text);
                }
                else if (@event is AttachmentAcceptedEvent attachment)
                    Console.WriteLine($"[附件] 已接收 {attachment.ContentKind} attachmentId={attachment.AttachmentId} assetId={attachment.AssetId ?? "-"} size={attachment.SizeBytes}");
                else if (@event is SystemNoticeEvent notice)
                    Console.WriteLine($"[提示] {notice.Notice}");
                else if (@event is ErrorEvent error)
                    Console.WriteLine($"[错误] {error.Error}");
            }
        }
    }

    private async Task<bool> InitializeGameAsync()
    {
        Console.Write("请输入你的名字：");
        var playerName = Console.ReadLine()?.Trim() ?? "玩家";

        var archetypes = await _sessionCatalog.GetAvailableArchetypesAsync();
        if (archetypes.Count == 0)
        {
            Console.WriteLine("[错误] 未找到任何角色原型定义。");
            return false;
        }

        Console.WriteLine("\n选择角色原型：");
        for (int i = 0; i < archetypes.Count; i++)
            Console.WriteLine($"  {i + 1}. {archetypes[i].Name}");
        Console.Write($"请选择（1-{archetypes.Count}）：");

        var choice = Console.ReadLine()?.Trim();
        var index = int.TryParse(choice, out var n) && n >= 1 && n <= archetypes.Count ? n - 1 : 0;
        var selectedArchetype = archetypes[index];

        await _session.InitGameAsync(selectedArchetype.Id, playerName);
        _state = await _session.TryGetStateAsync();
        Console.WriteLine("\n游戏开始！输入 /help 查看所有指令。\n");
        return true;
    }

    private static void PrintMessageOutput(MessageEvent message)
    {
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    Console.WriteLine(text.Text);
                    break;
                case ImageBlock image:
                    Console.WriteLine($"[图片] assetId={image.AssetId} mime={image.MimeType} name={image.Name ?? "-"} caption={image.Caption ?? "-"}");
                    break;
                case AssetBlock asset:
                    Console.WriteLine($"[媒体] assetId={asset.AssetId} mime={asset.MimeType} name={asset.Name ?? "-"}");
                    break;
                default:
                    Console.WriteLine($"[媒体] 不支持的输出类型：{block.Kind}");
                    break;
            }
        }
    }

    private static string StepLabel(string nodeKind) => nodeKind switch
    {
        "Memory" => "记忆",
        "Executor" => "规划",
        "Capability" => "执行",
        "State" => "状态",
        "Terminal" => "完成",
        _ => nodeKind
    };
}
