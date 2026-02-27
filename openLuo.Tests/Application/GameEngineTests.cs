using openLuo.Modules.Content.Application.Loaders;
using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Application.Validation;
using openLuo.Modules.Content.Core.Definitions;
using openLuo.Modules.Agent.Application;
using openLuo.Modules.Agent.Core.Interfaces;
using openLuo.Modules.Agent.Core.Models;
using openLuo.Modules.Gameplay.Application.Services;
using openLuo.Core.Interfaces;
using openLuo.Modules.Commanding.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Memory.Core.Interfaces;
using openLuo.Modules.SessionRuntime.Application;
using openLuo.Modules.SessionRuntime.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models;

namespace openLuo.Application.Tests;

public class GameEngineTests
{
    private const string DefaultGameId = "g1";
    private readonly IGameStateRepository _stateRepo = Substitute.For<IGameStateRepository>();
    private readonly ICharacterRepository _characterRepo = Substitute.For<ICharacterRepository>();
    private readonly IPluginHost _plugins = Substitute.For<IPluginHost>();
    private readonly IPlayerChatDispatcher _playerChat = Substitute.For<IPlayerChatDispatcher>();
    private readonly IAgentCommandBridge _commandBridge = Substitute.For<IAgentCommandBridge>();
    private readonly IMultiCharacterOrchestrator _multiCharacter = Substitute.For<IMultiCharacterOrchestrator>();
    private readonly IPartyTaskRepository _partyTaskRepo = Substitute.For<IPartyTaskRepository>();
    private readonly ICommandConfirmationService _confirmation = Substitute.For<ICommandConfirmationService>();
    private readonly IStateStore _stateStore = Substitute.For<IStateStore>();
    private readonly IMemoryWriteService _memoryWriteService = Substitute.For<IMemoryWriteService>();

    public GameEngineTests()
    {
        _commandBridge.ExecuteAsync(
                Arg.Any<string>(),
                Arg.Any<string[]>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<GameBridgeRequestContext?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                _plugins.ExecutePluginCommandAsync(
                    call.ArgAt<string>(0),
                    new
                    {
                        args = call.ArgAt<string[]>(1),
                        options = call.ArgAt<Dictionary<string, string>>(2),
                        characterId = call.ArgAt<string>(3)
                    },
                    call.ArgAt<CancellationToken>(6),
                    call.ArgAt<string?>(5)));

        _partyTaskRepo.CreateTaskAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns("task_test");
        _partyTaskRepo.CreateStepsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<PartyTaskStepRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _partyTaskRepo.UpdateStepResultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _partyTaskRepo.UpdateTaskStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private static string Game(string input) => input;

    private GameEngine CreateEngine(
        List<CharacterArchetypeDefinition>? backgrounds = null,
        ICommandGate? gate = null,
        IMultiCharacterOrchestrator? multiCharacter = null) =>
        new(
            _stateRepo,
            _characterRepo,
            BuildBootstrapper(backgrounds ?? []),
            new AgentInvocationRouter(
                _playerChat,
                _commandBridge,
                multiCharacter ?? _multiCharacter,
                _partyTaskRepo,
                _confirmation),
            null,
            gate);

    private ContentRegistry BuildRegistry(IReadOnlyList<CharacterArchetypeDefinition> backgrounds)
    {
        var builder = new ContentRegistryBuilder(new BasicContentValidator());
        var pack = new PackManifest
        {
            Id = "test.archetypes",
            DisplayName = "Test Archetypes",
            Contents = backgrounds.Select(background => new PackContentRef
            {
                Kind = ContentKind.CharacterArchetype,
                Id = background.Id
            }).ToList()
        };
        builder.AddPack(pack);
        foreach (var background in backgrounds)
            builder.AddDefinition(background, pack.Id);
        return builder.Build();
    }

    private ISessionBootstrapper BuildBootstrapper(IReadOnlyList<CharacterArchetypeDefinition> backgrounds) =>
        new ContentRegistrySessionBootstrapper(
            BuildRegistry(backgrounds),
            _stateRepo,
            _characterRepo,
            _stateStore,
            _memoryWriteService);

    [Fact]
    public async Task ExecuteAsync_NotSlashCommand_ReturnsInputGuidanceError()
    {
        var engine = CreateEngine();

        var result = await engine.ExecuteAsync(DefaultGameId, Game("chat hi"));

        Assert.False(result.Success);
        Assert.Contains("请输入有效的指令", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCommand_ReturnsUnknownCommandError()
    {
        _commandBridge.GetCommands().Returns([]);
        var engine = CreateEngine();

        var result = await engine.ExecuteAsync(DefaultGameId, Game("/unknown"));

        Assert.False(result.Success);
        Assert.Equal("未知指令：/unknown。输入 /help 查看所有指令。", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_TaskCommand_HandledByMultiCharacterWithoutPluginCommand()
    {
        _commandBridge.GetCommands().Returns([]);
        _multiCharacter.CanHandle(Arg.Is<ParsedCommand>(x => x.Name == "task" && x.Kind == InvocationKind.Command)).Returns(true);
        _multiCharacter.ExecuteAsync(Arg.Any<MultiCharacterCommandContext>(), Arg.Any<CancellationToken>())
            .Returns(CommandResult.Ok("任务协作开始"));

        var state = new GameState { Id = "g1", ArchetypeId = "bg1", PlayerName = "P" };
        var character = new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>()).Returns(character);

        var result = await CreateEngine(multiCharacter: _multiCharacter).ExecuteAsync("g1", Game("/task 帮我做计划"));

        Assert.True(result.Success);
        Assert.Equal("任务协作开始", result.Output);
        await _multiCharacter.Received(1).ExecuteAsync(
            Arg.Is<MultiCharacterCommandContext>(ctx =>
                ctx.CommandName == "task" &&
                ctx.ActiveCharacter.Id == "c1" &&
                ctx.State.Id == "g1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PluginAliasCommand_ForwardsArgsOptionsAndCharacterId()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "gift", Aliases = ["送礼"] }
        ]);

        var state = new GameState { Id = "g1", ArchetypeId = "bg1", PlayerName = "P" };
        var character = new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>()).Returns(character);

        object? capturedArgs = null;
        _plugins.ExecutePluginCommandAsync(
                "送礼",
                Arg.Do<object>(x => capturedArgs = x),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(CommandResult.Ok("done"));

        var engine = CreateEngine();
        var result = await engine.ExecuteAsync("g1", Game("/送礼 flower --mood happy --count 2"));

        Assert.True(result.Success);
        Assert.Equal("done", result.Output);
        await _plugins.Received(1).ExecutePluginCommandAsync("送礼", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());

        Assert.NotNull(capturedArgs);
        var type = capturedArgs!.GetType();
        var args = Assert.IsType<string[]>(type.GetProperty("args")!.GetValue(capturedArgs));
        var options = Assert.IsAssignableFrom<Dictionary<string, string>>(type.GetProperty("options")!.GetValue(capturedArgs));
        var characterId = Assert.IsType<string>(type.GetProperty("characterId")!.GetValue(capturedArgs));

        Assert.Equal(["flower"], args);
        Assert.Equal("happy", options["mood"]);
        Assert.Equal("2", options["count"]);
        Assert.Equal("c1", characterId);
    }

    [Fact]
    public async Task ExecuteAsync_WithAsOption_UsesSelectedCharacterForPluginContext()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "chat", Aliases = [] }
        ]);

        var state = new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P",
            ActiveCharacterId = "char_bg2"
        };
        var selected = new Character { Id = "char_bg2", GameId = "g1", ArchetypeId = "bg2", Name = "大小姐" };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _characterRepo.GetByIdAsync("g1", "大小姐", Arg.Any<CancellationToken>()).Returns(selected);

        object? capturedArgs = null;
        _plugins.ExecutePluginCommandAsync(
                "chat",
                Arg.Do<object>(x => capturedArgs = x),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(CommandResult.Ok("ok"));

        var engine = CreateEngine();
        var result = await engine.ExecuteAsync("g1", Game("/chat 你好 --as 大小姐"));

        Assert.True(result.Success);
        Assert.NotNull(capturedArgs);
        var characterId = (string)capturedArgs!.GetType().GetProperty("characterId")!.GetValue(capturedArgs)!;
        Assert.Equal("char_bg2", characterId);
    }

    [Fact]
    public async Task ExecuteAsync_SkillPrefix_UsesSkillCategoryCommand()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "plan", Category = "skill", Prefix = "$" }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });
        _plugins.ExecutePluginCommandAsync("plan", Arg.Any<object>(), Arg.Any<CancellationToken>(), "skill")
            .Returns(CommandResult.Ok("ok"));

        var result = await CreateEngine().ExecuteAsync("g1", Game("$plan 今天安排"));

        Assert.True(result.Success);
        await _plugins.Received(1).ExecutePluginCommandAsync("plan", Arg.Any<object>(), Arg.Any<CancellationToken>(), "skill");
    }

    [Fact]
    public async Task ExecuteAsync_SubAgentPrefix_UsesSubAgentCategoryCommand()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "executor", Category = "subagent", Prefix = "&" }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });
        _plugins.ExecutePluginCommandAsync("executor", Arg.Any<object>(), Arg.Any<CancellationToken>(), "subagent")
            .Returns(CommandResult.Ok("ok"));

        var result = await CreateEngine().ExecuteAsync("g1", Game("&executor 执行任务"));

        Assert.True(result.Success);
        await _plugins.Received(1).ExecutePluginCommandAsync("executor", Arg.Any<object>(), Arg.Any<CancellationToken>(), "subagent");
    }

    [Fact]
    public async Task ExecuteAsync_ToolPrefix_UsesToolCategoryCommand()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "bash", Category = "tool", Prefix = "@" }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });
        _plugins.ExecutePluginCommandAsync("bash", Arg.Any<object>(), Arg.Any<CancellationToken>(), "tool")
            .Returns(CommandResult.Ok("ok"));

        var result = await CreateEngine().ExecuteAsync("g1", Game("@bash pwd"));

        Assert.True(result.Success);
        await _plugins.Received(1).ExecutePluginCommandAsync("bash", Arg.Any<object>(), Arg.Any<CancellationToken>(), "tool");
    }

    [Fact]
    public async Task ExecuteAsync_SkillNeedsConfirm_WithoutConfirm_BlocksExecution()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "plan", Category = "skill", Prefix = "$", NeedsConfirm = true, RiskLevel = "high" }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        var result = await CreateEngine().ExecuteAsync("g1", Game("$plan 先做计划"));

        Assert.False(result.Success);
        Assert.Contains("已取消执行", result.Error);
        await _plugins.DidNotReceive().ExecutePluginCommandAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ExecuteAsync_SkillNeedsConfirm_WithConfirm_Executes()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "plan", Category = "skill", Prefix = "$", NeedsConfirm = true, RiskLevel = "high" }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });
        _plugins.ExecutePluginCommandAsync("plan", Arg.Any<object>(), Arg.Any<CancellationToken>(), "skill")
            .Returns(CommandResult.Ok("ok"));

        var result = await CreateEngine().ExecuteAsync("g1", Game("$plan 先做计划 --confirm yes"));

        Assert.True(result.Success);
        Assert.Contains("任务ID", result.Output);
        await _plugins.Received(1).ExecutePluginCommandAsync("plan", Arg.Any<object>(), Arg.Any<CancellationToken>(), "skill");
    }

    [Fact]
    public async Task ExecuteAsync_StatusCommand_ByName_ForwardsCharacterContext()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "status", Aliases = [] }
        ]);

        var state = new GameState { Id = "g1", ArchetypeId = "bg1", PlayerName = "P" };
        var character = new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" };
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(state);
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>()).Returns(character);

        object? capturedArgs = null;
        _plugins.ExecutePluginCommandAsync(
                "status",
                Arg.Do<object>(x => capturedArgs = x),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(CommandResult.Ok("status panel"));

        var engine = CreateEngine();
        var result = await engine.ExecuteAsync("g1", Game("/status"));

        Assert.True(result.Success);
        Assert.Equal("status panel", result.Output);
        await _plugins.Received(1).ExecutePluginCommandAsync("status", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());

        Assert.NotNull(capturedArgs);
        var type = capturedArgs!.GetType();
        var args = Assert.IsType<string[]>(type.GetProperty("args")!.GetValue(capturedArgs));
        var options = Assert.IsAssignableFrom<Dictionary<string, string>>(type.GetProperty("options")!.GetValue(capturedArgs));
        var characterId = Assert.IsType<string>(type.GetProperty("characterId")!.GetValue(capturedArgs));

        Assert.Empty(args);
        Assert.Empty(options);
        Assert.Equal("c1", characterId);
    }

    [Fact]
    public async Task ExecuteAsync_StatusCommand_WithoutState_ThrowsNotInitialized()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "status", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns((GameState?)null);

        var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteAsync("g1", Game("/status")));
        Assert.Equal("游戏未初始化。", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBlockedByCommandGate_ReturnsFailWithoutExecutingPlugin()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "chat", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        var gate = Substitute.For<ICommandGate>();
        gate.BeforeExecuteAsync(Arg.Any<CommandGateContext>(), Arg.Any<CancellationToken>())
            .Returns(new CommandGateBeforeResult
            {
                Allow = false,
                Message = "当前时段不允许聊天。"
            });

        var engine = CreateEngine(gate: gate);
        var result = await engine.ExecuteAsync("g1", Game("/chat 你好"));

        Assert.False(result.Success);
        Assert.Equal("当前时段不允许聊天。", result.Error);
        await _plugins.DidNotReceive().ExecutePluginCommandAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenBlockedByCommandGate_SanitizesBlockMessage()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "chat", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        var gate = Substitute.For<ICommandGate>();
        gate.BeforeExecuteAsync(Arg.Any<CommandGateContext>(), Arg.Any<CancellationToken>())
            .Returns(new CommandGateBeforeResult
            {
                Allow = false,
                Message = "\u001b[31m危险提示\u001b[0m"
            });

        var result = await CreateEngine(gate: gate).ExecuteAsync("g1", Game("/chat 你好"));

        Assert.False(result.Success);
        Assert.Equal("危险提示", result.Error);
        Assert.DoesNotContain("\u001b[", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGateHasNotices_PrependsNoticeToOutput()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "chat", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        var gate = Substitute.For<ICommandGate>();
        gate.BeforeExecuteAsync(Arg.Any<CommandGateContext>(), Arg.Any<CancellationToken>())
            .Returns(new CommandGateBeforeResult
            {
                Allow = true,
                Notices = ["⏰ 约会计划到期了。"]
            });
        _plugins.ExecutePluginCommandAsync("chat", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(CommandResult.Ok("角色回复"));

        var engine = CreateEngine(gate: gate);
        var result = await engine.ExecuteAsync("g1", Game("/chat 你好"));

        Assert.True(result.Success);
        Assert.Contains("约会计划到期了", result.Output);
        Assert.Contains("角色回复", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBlockedAndGateHasNotices_IncludesNoticesInError()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "chat", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        var gate = Substitute.For<ICommandGate>();
        gate.BeforeExecuteAsync(Arg.Any<CommandGateContext>(), Arg.Any<CancellationToken>())
            .Returns(new CommandGateBeforeResult
            {
                Allow = false,
                Message = "当前时段不允许聊天。",
                Notices = ["⏰ 你有新的日程提醒。"]
            });

        var result = await CreateEngine(gate: gate).ExecuteAsync("g1", Game("/chat 你好"));

        Assert.False(result.Success);
        Assert.Contains("日程提醒", result.Error);
        Assert.Contains("当前时段不允许聊天", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPluginFailsAndGateHasNotices_PrependsNoticesToError()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "work", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        var gate = Substitute.For<ICommandGate>();
        gate.BeforeExecuteAsync(Arg.Any<CommandGateContext>(), Arg.Any<CancellationToken>())
            .Returns(new CommandGateBeforeResult
            {
                Allow = true,
                Notices = ["⏰ 约会计划到点了。"]
            });
        _plugins.ExecutePluginCommandAsync("work", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(CommandResult.Fail("工作失败"));

        var result = await CreateEngine(gate: gate).ExecuteAsync("g1", Game("/work"));

        Assert.False(result.Success);
        Assert.Contains("约会计划到点了", result.Error);
        Assert.Contains("工作失败", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_SanitizesPluginOutputAndError()
    {
        _commandBridge.GetCommands().Returns([
            new CommandDescriptor { Name = "chat", Aliases = [] },
            new CommandDescriptor { Name = "work", Aliases = [] }
        ]);
        _stateRepo.GetAsync("g1", Arg.Any<CancellationToken>()).Returns(new GameState
        {
            Id = "g1",
            ArchetypeId = "bg1",
            PlayerName = "P"
        });
        _characterRepo.GetByArchetypeIdAsync("bg1", Arg.Any<CancellationToken>())
            .Returns(new Character { Id = "c1", ArchetypeId = "bg1", Name = "铃" });

        _plugins.ExecutePluginCommandAsync("chat", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(CommandResult.Ok("\u001b[32m角色回复\u001b[0m"));
        _plugins.ExecutePluginCommandAsync("work", Arg.Any<object>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(CommandResult.Fail("\u001b[31m失败\u001b[0m"));

        var engine = CreateEngine();
        var ok = await engine.ExecuteAsync("g1", Game("/chat 你好"));
        var fail = await engine.ExecuteAsync("g1", Game("/work"));

        Assert.True(ok.Success);
        Assert.Equal("角色回复", ok.Output);
        Assert.DoesNotContain("\u001b[", ok.Output);

        Assert.False(fail.Success);
        Assert.Equal("失败", fail.Error);
        Assert.DoesNotContain("\u001b[", fail.Error);
    }

    [Fact]
    public async Task InitializeAsync_BackgroundExists_UsesBackgroundCharacterName()
    {
        GameState? savedState = null;
        Character? savedCharacter = null;
        _stateRepo.SaveAsync(Arg.Do<GameState>(s => savedState = s), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _characterRepo.SaveAsync(Arg.Do<Character>(c => savedCharacter = c), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var engine = CreateEngine([
            new CharacterArchetypeDefinition { Id = "bg1", DisplayName = "测试原型", CharacterName = "朝霞" }
        ]);

        await engine.InitializeAsync("g1", "bg1", "玩家");

        Assert.NotNull(savedState);
        Assert.Equal("玩家", savedState!.PlayerName);
        Assert.Equal("bg1", savedState.ArchetypeId);
        Assert.Equal(1, savedState.CurrentDay);

        Assert.NotNull(savedCharacter);
        Assert.Equal("朝霞", savedCharacter!.Name);
        Assert.Equal("bg1", savedCharacter.ArchetypeId);
    }

    [Fact]
    public async Task InitializeAsync_BackgroundMissing_UsesUnknownCharacterNameFallback()
    {
        Character? savedCharacter = null;
        _stateRepo.SaveAsync(Arg.Any<GameState>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _characterRepo.SaveAsync(Arg.Do<Character>(c => savedCharacter = c), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var engine = CreateEngine();

        await engine.InitializeAsync("g1", "missing-bg", "玩家");

        Assert.NotNull(savedCharacter);
        Assert.Equal("未知", savedCharacter!.Name);
    }
}
