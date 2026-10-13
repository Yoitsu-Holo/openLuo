using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using openLuo.Client;
using openLuo.Protocol;
using Xunit;

namespace openLuo.Cli.Tests;

/// <summary>
/// 客户端身份贯穿（<c>OPENLUO_USER_ID</c> / <c>OPENLUO_THREAD_ID</c>）：Hub 需要 <c>UserId</c> 才能把回合
/// 归因到**说话人**（落库 <c>speaker_id</c>、记忆按 User 域分区），需要 <c>ThreadId</c> 才能区分同一会话内的
/// 并行话题。三端（CLI / TUI / GUI）都必须把这两个值贯到回合请求里。
///
/// 断言的是**线级证据**（MiniWsServer 收到真实的 <c>turn.submit</c> 帧），不是中间对象：
/// 设了值 → 帧里带 <c>userId</c>/<c>threadId</c>；不设 → 帧里**没有**这两个键
/// （协议 JSON 的 <c>WhenWritingNull</c> 让"不传"是字面意义的，而非送个 null 过去）。
/// </summary>
public sealed class TurnIdentityTests
{
    private const string UserId = "u-10086";
    private const string ThreadId = "thread-7";

    /// <summary>Hub 桩：握手 → 开会话 → 收 <c>turn.submit</c>（数据段收集起来）并立即回 <c>turn.final</c>。</summary>
    private static MiniWsServer StartHub(ConcurrentQueue<JsonNode?> turns) => new(async (connection, json) =>
    {
        var root = JsonNode.Parse(json)!.AsObject();
        switch (root["type"]?.GetValue<string>())
        {
            case MessageTypes.Hello:
                await connection.SendTextAsync(ProtocolJson.Serialize(
                    EnvelopeFactory.Create(EventTypes.Welcome, new WelcomeEvent { ServerVersion = "test" })));
                break;
            case MessageTypes.SessionOpen:
                await connection.SendTextAsync(ProtocolJson.Serialize(
                    EnvelopeFactory.Create(EventTypes.SessionOpened, new SessionDto { SessionId = "s-identity" })));
                break;
            case MessageTypes.TurnSubmit:
                turns.Enqueue(root["data"]);
                await connection.SendTextAsync(ProtocolJson.Serialize(
                    EnvelopeFactory.Create(EventTypes.TurnFinal, new TurnResultDto
                    {
                        TurnId = "t1", Success = true, FinalText = "好",
                    })));
                break;
        }
    });

    private static JsonNode Received(ConcurrentQueue<JsonNode?> turns) =>
        Assert.Single(turns) ?? throw new InvalidOperationException("hub 桩没收到 turn.submit");

    [Fact]
    public async Task Cli_turn_carries_identity_when_configured()
    {
        var turns = new ConcurrentQueue<JsonNode?>();
        await using var hub = StartHub(turns);

        await new openLuo.Cli.CliHubApp(hub.StreamUrl, "player", "demo",
                writeLine: _ => Task.CompletedTask, conversationId: null, userId: UserId, threadId: ThreadId)
            .RunAsync(new StringReader("你好\n"));

        var data = Received(turns);
        Assert.Equal("cli", data["sourceId"]!.GetValue<string>());
        Assert.Equal(UserId, data["userId"]!.GetValue<string>());
        Assert.Equal(ThreadId, data["threadId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cli_turn_omits_identity_when_unset()
    {
        var turns = new ConcurrentQueue<JsonNode?>();
        await using var hub = StartHub(turns);

        await new openLuo.Cli.CliHubApp(hub.StreamUrl, "player", "demo", writeLine: _ => Task.CompletedTask)
            .RunAsync(new StringReader("你好\n"));

        var data = Received(turns);
        // 没设环境变量 = 完全不出现这两个键（不是送了 null）
        Assert.Null(data["userId"]);
        Assert.Null(data["threadId"]);
    }

    [Fact]
    public async Task Gui_turn_carries_identity_when_configured()
    {
        var turns = new ConcurrentQueue<JsonNode?>();
        await using var hub = StartHub(turns);

        var viewModel = new openLuo.Gui.GuiMainViewModel(hub.StreamUrl, "player", "demo", null, UserId, ThreadId);
        await viewModel.SendAsync("你好");

        var data = Received(turns);
        Assert.Equal("gui", data["sourceId"]!.GetValue<string>());
        Assert.Equal(UserId, data["userId"]!.GetValue<string>());
        Assert.Equal(ThreadId, data["threadId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Gui_turn_omits_identity_when_unset()
    {
        var turns = new ConcurrentQueue<JsonNode?>();
        await using var hub = StartHub(turns);

        var viewModel = new openLuo.Gui.GuiMainViewModel(hub.StreamUrl, "player", "demo");
        await viewModel.SendAsync("你好");

        var data = Received(turns);
        Assert.Null(data["userId"]);
        Assert.Null(data["threadId"]);
    }

    /// <summary>
    /// TUI 的输入循环依赖 Terminal.Gui 初始化（没有 TTY 跑不起来），无法像 CLI/GUI 那样在测试里跑到线上，
    /// 因此断言 <c>HandleAsync</c> 实际使用的那个构造方法（<see cref="openLuo.Tui.TuiApplication.BuildTurnRequest"/>）。
    /// </summary>
    [Fact]
    public void Tui_turn_request_carries_identity_when_configured()
    {
        var app = new openLuo.Tui.TuiApplication("ws://127.0.0.1:1/v1/stream", "player", "demo", null, UserId, ThreadId);

        var request = app.BuildTurnRequest("你好");
        Assert.Equal("tui", request.SourceId);
        Assert.Equal(UserId, request.UserId);
        Assert.Equal(ThreadId, request.ThreadId);
    }

    [Fact]
    public void Tui_turn_request_omits_identity_when_unset()
    {
        var app = new openLuo.Tui.TuiApplication("ws://127.0.0.1:1/v1/stream", "player", "demo");

        var request = app.BuildTurnRequest("你好");
        Assert.Null(request.UserId);
        Assert.Null(request.ThreadId);
    }

    /// <summary>
    /// 真实二进制实测：<c>Program.cs</c> 里的"读环境变量 → 传给构造点"这一环（顶级语句，进程内无法单测）。
    /// 另外三端与 CLI 的 Program.cs 是同一段写法，但 TUI 要终端、GUI 要显示服务，无 TTY 的环境只能跑 CLI。
    /// </summary>
    [Fact]
    public async Task Cli_binary_reads_identity_from_environment()
    {
        var turns = new ConcurrentQueue<JsonNode?>();
        await using var hub = StartHub(turns);

        var launcher = FindCliLauncher();
        Assert.False(launcher is null, "未找到 CLI 产物（openLuo.Cli/bin/<配置>/net10.0/openluo-cli）——本测试要求 CLI 已构建");

        var start = new ProcessStartInfo(launcher!.Value.FileName);
        if (Path.GetDirectoryName(launcher.Value.FileName) is { Length: > 0 } dir)
            start.WorkingDirectory = dir;
        foreach (var arg in launcher.Value.Args)
            start.ArgumentList.Add(arg);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.Environment["OPENLUO_HUB_URL"] = hub.StreamUrl;
        start.Environment["OPENLUO_USER_ID"] = UserId;
        start.Environment["OPENLUO_THREAD_ID"] = ThreadId;

        using var process = Process.Start(start)!;
        // 先起抽干任务：管道写满会把子进程卡住（此处输出很少，但抽干是更便宜的正确做法）
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync("你好");
        process.StandardInput.Close();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }

        var data = Received(turns);
        Assert.True(process.ExitCode == 0, $"CLI 退出码 {process.ExitCode}\n{await stdout}\n{await stderr}");
        Assert.Equal(UserId, data["userId"]!.GetValue<string>());
        Assert.Equal(ThreadId, data["threadId"]!.GetValue<string>());
    }

    /// <summary>定位 CLI 产物：优先原生 apphost（可直接执行），退化为 `dotnet openluo-cli.dll`。</summary>
    private static (string FileName, List<string> Args)? FindCliLauncher()
    {
        // 测试产物目录形如 <repo>/tests/openLuo.Cli.Tests/bin/<配置>/<tfm>/
        var tfm = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "openLuo.slnx")))
            root = root.Parent;
        if (root is null)
            return null;

        foreach (var config in new[] { "Debug", "Release" })
        {
            var output = Path.Combine(root.FullName, "openLuo.Cli", "bin", config, tfm);
            var apphost = Path.Combine(output, "openluo-cli");
            if (File.Exists(apphost))
                return (apphost, []);

            var dll = Path.Combine(output, "openluo-cli.dll");
            if (File.Exists(dll))
                return ("dotnet", [dll]);
        }

        return null;
    }
}
