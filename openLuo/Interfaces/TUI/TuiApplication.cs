using System.Text;
using Terminal.Gui;
using openLuo.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Infrastructure.IO;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Core.Models;
using TuiApp = Terminal.Gui.Application;
using TuiAttribute = Terminal.Gui.Attribute;

namespace openLuo.Interfaces.TUI;

public class TuiApplication
{
    private const string SystemChannel = "__system__";

    private readonly IGameSessionCatalog _sessionCatalog;
    private readonly IGameSession _session;
    private readonly TuiStreams _streams;
    private readonly SemaphoreSlim _interactionLock = new(1, 1);
    private readonly Dictionary<string, List<string>> _dialogues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Character> _characters = [];

    private TextView _dialogueView = null!;
    private TextView _statusView = null!;
    private TextField _inputField = null!;
    private FrameView _characterFrame = null!;
    private FrameView _statusFrame = null!;
    private FrameView _dialogueFrame = null!;

    private GameState? _state;
    private string? _selectedCharacterId;
    private int _initStep;
    private string _pendingPlayerName = string.Empty;
    private List<SessionArchetypeOption> _archetypes = [];
    private bool _startupScheduled;

    public TuiApplication(
        IGameSessionCatalog sessionCatalog,
        IGameSession session,
        IGameStreams streams,
        GameState? state)
    {
        _sessionCatalog = sessionCatalog;
        _session = session;
        _streams = (TuiStreams)streams;
        _state = state;
    }

    public async Task RunAsync()
    {
        TuiApp.Init();

        var top = TuiApp.Top;
        top.ColorScheme = new ColorScheme
        {
            Normal = TuiAttribute.Make(Color.White, Color.Black),
            Focus = TuiAttribute.Make(Color.White, Color.Black),
            HotNormal = TuiAttribute.Make(Color.BrightCyan, Color.Black),
            HotFocus = TuiAttribute.Make(Color.BrightYellow, Color.Black),
            Disabled = TuiAttribute.Make(Color.DarkGray, Color.Black)
        };

        BuildLayout(top);
        ResetPanelsForInitialization();

        _inputField.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key != Key.Enter)
                return;

            var input = _inputField.Text?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(input))
            {
                _inputField.Text = string.Empty;
                Task.Run(() => HandleInputAsync(input));
            }

            e.Handled = true;
        };

        top.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == (Key.Q | Key.CtrlMask))
            {
                TuiApp.RequestStop();
                e.Handled = true;
            }
        };

        _inputField.SetFocus();
        TuiApp.MainLoop.AddIdle(() =>
        {
            if (_startupScheduled)
                return false;

            _startupScheduled = true;
            WireStreams();
            _ = Task.Run(InitializeAfterLoopStartAsync);
            return false;
        });
        TuiApp.Run();
        TuiApp.Shutdown();
    }

    private async Task InitializeAfterLoopStartAsync()
    {
        if (_state is not null)
            await RefreshRosterAndStatusAsync(preferActiveSelection: true);
        else
            TuiApp.MainLoop.Invoke(ResetPanelsForInitialization);
    }

    private void BuildLayout(Toplevel top)
    {
        var leftWidth = Dim.Percent(30);
        var topHeight = Dim.Percent(68);

        _characterFrame = new FrameView(" Roles ")
        {
            X = 0,
            Y = 0,
            Width = leftWidth,
            Height = topHeight
        };

        _statusFrame = new FrameView(" Status ")
        {
            X = 0,
            Y = Pos.Bottom(_characterFrame),
            Width = leftWidth,
            Height = Dim.Fill()
        };

        _dialogueFrame = new FrameView(" Dialogue ")
        {
            X = Pos.Right(_characterFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = topHeight
        };

        var inputFrame = new FrameView(" Input ")
        {
            X = Pos.Right(_characterFrame),
            Y = Pos.Bottom(_dialogueFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _dialogueView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
            Text = _state is null ? "欢迎！请输入你的名字：\n" : "游戏开始！请输入指令...\n"
        };

        _statusView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
            Text = _state is null ? "等待游戏初始化..." : "正在加载角色状态..."
        };

        _inputField = new TextField
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        var hint = new Label("Enter 发送，Ctrl+Q 退出")
        {
            X = 0,
            Y = Pos.Bottom(_inputField) + 1
        };

        _dialogueFrame.Add(_dialogueView);
        _statusFrame.Add(_statusView);
        inputFrame.Add(_inputField, hint);
        top.Add(_characterFrame, _statusFrame, _dialogueFrame, inputFrame);
    }

    private void WireStreams()
    {
        _streams.OutputReceived += text => TuiApp.MainLoop.Invoke(() =>
        {
            AppendDialogue(text, GetCurrentDialogueKey());
        });

        _streams.ErrorReceived += text => TuiApp.MainLoop.Invoke(() =>
        {
            AppendDialogue($"[错误流] {EnsureTrailingNewline(text)}", GetCurrentDialogueKey());
        });

        _streams.StartMonitoring();
    }

    private async Task HandleInputAsync(string input)
    {
        await _interactionLock.WaitAsync();
        try
        {
            if (_state is null)
            {
                await HandleInitializationInputAsync(input);
                return;
            }

            var dialogueKey = GetCurrentDialogueKey();
            TuiApp.MainLoop.Invoke(() => AppendDialogue($"> {input}\n", dialogueKey));
            await EnsureSelectedCharacterActiveAsync();

            var result = await _session.SubmitAsync(new GameSessionInput
            {
                SourceId = "tui",
                ChannelId = "main",
                ActorId = "player",
                Kind = SessionInputKind.Text,
                Text = input
            });

            await RefreshRosterAndStatusAsync(preferActiveSelection: true);
            TuiApp.MainLoop.Invoke(() => ApplyGameEvents(result.Events, dialogueKey));
        }
        finally
        {
            _interactionLock.Release();
        }
    }

    private async Task HandleInitializationInputAsync(string input)
    {
        if (_initStep == 0)
        {
            _pendingPlayerName = input.Trim();
            TuiApp.MainLoop.Invoke(() => AppendDialogue($"> {_pendingPlayerName}\n", SystemChannel));

            _archetypes = (await _sessionCatalog.GetAvailableArchetypesAsync()).ToList();
            var builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine("选择角色原型：");
            for (int i = 0; i < _archetypes.Count; i++)
                builder.AppendLine($"  {i + 1}. {_archetypes[i].Name}");
            builder.AppendLine();

            if (_archetypes.Count == 0)
            {
                builder.AppendLine("[错误] 未找到任何角色原型定义。");
                TuiApp.MainLoop.Invoke(() => AppendDialogue(builder.ToString(), SystemChannel));
                return;
            }

            builder.AppendLine($"请选择（1-{_archetypes.Count}）：");
            _initStep = 1;
            TuiApp.MainLoop.Invoke(() => AppendDialogue(builder.ToString(), SystemChannel));
            return;
        }

        TuiApp.MainLoop.Invoke(() => AppendDialogue($"> {input}\n", SystemChannel));
        var index = int.TryParse(input, out var n) && n >= 1 && n <= _archetypes.Count ? n - 1 : 0;
        var selectedArchetype = _archetypes[index];

        await _session.InitGameAsync(selectedArchetype.Id, _pendingPlayerName);
        _state = await _session.TryGetStateAsync();
        _initStep = 0;
        TuiApp.MainLoop.Invoke(() => AppendDialogue("\n游戏开始！输入 /help 查看所有指令。\n", SystemChannel));
        await RefreshRosterAndStatusAsync(preferActiveSelection: true);
    }

    private async Task RefreshRosterAndStatusAsync(bool preferActiveSelection)
    {
        _state = await _session.TryGetStateAsync();
        if (_state is null)
        {
            TuiApp.MainLoop.Invoke(ResetPanelsForInitialization);
            return;
        }

        var roster = await _session.GetSessionRosterAsync(CancellationToken.None);
        _characters.Clear();
        _characters.AddRange(roster.Select(item => new Character
        {
            Id = item.CharacterId,
            ArchetypeId = item.ArchetypeId,
            Name = item.Name,
            DisplayPriority = item.DisplayPriority,
            IsEnabled = item.IsEnabled
        }));

        if (_characters.Count == 0)
        {
            _selectedCharacterId = null;
        }
        else if (preferActiveSelection && !string.IsNullOrWhiteSpace(_state.ActiveCharacterId))
        {
            _selectedCharacterId = _characters
                .FirstOrDefault(c => string.Equals(c.Id, _state.ActiveCharacterId, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? _selectedCharacterId ?? _characters[0].Id;
        }
        else if (string.IsNullOrWhiteSpace(_selectedCharacterId) ||
                 _characters.All(c => !string.Equals(c.Id, _selectedCharacterId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedCharacterId = _characters[0].Id;
        }

        var statusText = await BuildStatusTextAsync();
        TuiApp.MainLoop.Invoke(() =>
        {
            RenderCharacterButtons();
            _statusView.Text = statusText;
            UpdateStatusTitle();
            UpdateDialogueView();
        });
    }

    private async Task<string> BuildStatusTextAsync()
    {
        if (_state is null)
            return "等待游戏初始化...";

        var selected = GetSelectedCharacter();
        if (selected is null)
            return $"Day {_state.CurrentDay}  Time {FormatMinute(_state.CurrentMinute)}\n\n当前没有可显示的角色。";

        var status = await _session.GetCharacterStatusSnapshotAsync(selected.Id);
        if (status is null)
            return $"Day {_state.CurrentDay}  Time {FormatMinute(_state.CurrentMinute)}\n\n当前角色状态不可用。";

        var builder = new StringBuilder();
        builder.AppendLine($"角色：{selected.Name}");
        builder.AppendLine($"ID：{selected.Id}");
        builder.AppendLine($"当前天数：{status.CurrentDay}");
        builder.AppendLine($"当前时间：{FormatMinute(status.CurrentMinute)}");
        builder.AppendLine($"当前激活：{(status.IsActive ? "是" : "否")}");

        string? currentGroup = null;
        foreach (var item in status.Items)
        {
            if (!string.Equals(currentGroup, item.Group, StringComparison.OrdinalIgnoreCase))
            {
                currentGroup = item.Group;
                builder.AppendLine();
                builder.AppendLine($"[{currentGroup}]");
            }

            var valueText = item.Type == "bar" && !string.IsNullOrWhiteSpace(item.Max)
                ? $"{item.Value}/{item.Max}"
                : string.IsNullOrWhiteSpace(item.Text) ? item.Value : item.Text;
            builder.AppendLine($"{item.Label}: {valueText}");
        }

        if (!string.IsNullOrWhiteSpace(status.AdditionalText))
        {
            builder.AppendLine();
            builder.AppendLine(status.AdditionalText);
        }

        return builder.ToString();
    }

    private void ResetPanelsForInitialization()
    {
        RenderCharacterButtons();
        _statusView.Text = "等待游戏初始化...";
        UpdateStatusTitle();
        UpdateDialogueView();
    }

    private void RenderCharacterButtons()
    {
        _characterFrame.RemoveAll();

        if (_characters.Count == 0)
        {
            _characterFrame.Add(new Label("暂无角色")
            {
                X = 0,
                Y = 0
            });
            return;
        }

        for (int i = 0; i < _characters.Count; i++)
        {
            var character = _characters[i];
            var button = new Button(BuildCharacterButtonText(character))
            {
                X = 0,
                Y = i,
                Width = Dim.Fill()
            };

            button.Clicked += () => Task.Run(() => HandleCharacterSelectionAsync(character.Id));
            _characterFrame.Add(button);
        }
    }

    private async Task HandleCharacterSelectionAsync(string characterId)
    {
        await _interactionLock.WaitAsync();
        try
        {
            _selectedCharacterId = characterId;
            TuiApp.MainLoop.Invoke(() =>
            {
                RenderCharacterButtons();
                UpdateDialogueView();
            });

            if (_state is null)
            {
                TuiApp.MainLoop.Invoke(UpdateStatusPanel);
                return;
            }

            await EnsureSelectedCharacterActiveAsync();
            await RefreshRosterAndStatusAsync(preferActiveSelection: true);
        }
        finally
        {
            _interactionLock.Release();
        }
    }

    private async Task EnsureSelectedCharacterActiveAsync()
    {
        var selected = GetSelectedCharacter();
        if (_state is null || selected is null)
            return;

        if (string.Equals(_state.ActiveCharacterId, selected.Id, StringComparison.OrdinalIgnoreCase))
            return;

        await _session.SetActiveCharacterAsync(selected.Id);
        _state = await _session.TryGetStateAsync();
    }

    private void ApplyGameEvents(IReadOnlyList<GameEvent> events, string dialogueKey)
    {
        var hasMessageOutput = events.Any(static e => e is MessageEvent);
        foreach (var @event in events)
        {
            switch (@event)
            {
                case StatusSnapshotEvent snapshot:
                    if (_state is not null)
                    {
                        _state.CurrentDay = snapshot.CurrentDay ?? _state.CurrentDay;
                        _state.CurrentMinute = snapshot.CurrentMinute ?? _state.CurrentMinute;
                        if (!string.IsNullOrWhiteSpace(snapshot.ActiveCharacterId))
                            _state.ActiveCharacterId = snapshot.ActiveCharacterId;
                    }
                    break;
                case ErrorEvent error:
                    AppendDialogue($"[错误] {error.Error}\n", dialogueKey);
                    break;
                case AttachmentAcceptedEvent attachment:
                    AppendDialogue($"[附件] 已接收 {attachment.ContentKind} attachmentId={attachment.AttachmentId} assetId={attachment.AssetId ?? "-"} size={attachment.SizeBytes}\n", dialogueKey);
                    break;
                case SystemNoticeEvent notice:
                    AppendDialogue($"[提示] {notice.Notice}\n", dialogueKey);
                    break;
                case MessageEvent message:
                    AppendDialogue(BuildMessageOutputText(message), dialogueKey);
                    break;
                case TextOutputEvent text when !string.IsNullOrWhiteSpace(text.Text):
                    if (!hasMessageOutput)
                        AppendDialogue(EnsureTrailingNewline(text.Text), dialogueKey);
                    break;
            }
        }

        UpdateStatusTitle();
        UpdateDialogueView();
    }

    private void AppendDialogue(string text, string dialogueKey)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!_dialogues.TryGetValue(dialogueKey, out var lines))
        {
            lines = [];
            _dialogues[dialogueKey] = lines;
        }

        lines.Add(text);
        if (dialogueKey == GetCurrentDialogueKey())
        {
            _dialogueView.Text = string.Concat(lines);
            _dialogueView.MoveEnd();
        }
    }

    private void UpdateDialogueView()
    {
        _dialogueFrame.Title = _state is null
            ? " Dialogue / Init "
            : $" Dialogue: {GetSelectedCharacter()?.Name ?? "None"} ";

        var key = GetCurrentDialogueKey();
        if (_dialogues.TryGetValue(key, out var lines) && lines.Count > 0)
            _dialogueView.Text = string.Concat(lines);
        else if (_state is null)
            _dialogueView.Text = "欢迎！请输入你的名字：\n";
        else
            _dialogueView.Text = "当前角色还没有对话记录。\n";

        _dialogueView.MoveEnd();
    }

    private void UpdateStatusPanel()
    {
        UpdateStatusTitle();
        _statusView.Text = _state is null
            ? "等待游戏初始化..."
            : $"Day {_state.CurrentDay}  Time {FormatMinute(_state.CurrentMinute)}\n\n当前没有可显示的角色。";
    }

    private void UpdateStatusTitle()
    {
        _statusFrame.Title = _state is null
            ? " Status "
            : $" Status: {GetSelectedCharacter()?.Name ?? "None"} ";
    }

    private Character? GetSelectedCharacter() =>
        _characters.FirstOrDefault(c => string.Equals(c.Id, _selectedCharacterId, StringComparison.OrdinalIgnoreCase));

    private string GetCurrentDialogueKey()
    {
        if (_state is null)
            return SystemChannel;

        return GetSelectedCharacter()?.Id
            ?? (!string.IsNullOrWhiteSpace(_state.ActiveCharacterId) ? _state.ActiveCharacterId : SystemChannel);
    }

    private string BuildCharacterButtonText(Character character)
    {
        var selected = string.Equals(character.Id, _selectedCharacterId, StringComparison.OrdinalIgnoreCase);
        var active = _state is not null && string.Equals(character.Id, _state.ActiveCharacterId, StringComparison.OrdinalIgnoreCase);
        var prefix = selected ? "[*]" : "[ ]";
        var suffix = active ? " <active>" : string.Empty;
        return $"{prefix} {character.Name}{suffix}";
    }

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + "\n";

    private static string BuildMessageOutputText(MessageEvent message)
    {
        var parts = new List<string>();
        foreach (var block in message.Blocks)
        {
            switch (block)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    parts.Add(text.Text.TrimEnd());
                    break;
                case ImageBlock image:
                    parts.Add($"[图片] assetId={image.AssetId} mime={image.MimeType} name={image.Name ?? "-"} caption={image.Caption ?? "-"}");
                    break;
                case AssetBlock asset:
                    parts.Add($"[媒体] assetId={asset.AssetId} mime={asset.MimeType} name={asset.Name ?? "-"}");
                    break;
                default:
                    parts.Add($"[媒体] 不支持的输出类型：{block.Kind}");
                    break;
            }
        }

        return EnsureTrailingNewline(string.Join("\n", parts.Where(static x => !string.IsNullOrWhiteSpace(x))));
    }

    private static string FormatMinute(int minute)
    {
        var normalized = ((minute % 1440) + 1440) % 1440;
        return $"{normalized / 60:D2}:{normalized % 60:D2}";
    }
}
