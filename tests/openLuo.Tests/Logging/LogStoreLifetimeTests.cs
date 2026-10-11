using openLuo.Infrastructure.Logging;
using Xunit;

namespace openLuo.Tests.Logging;

/// <summary>
/// LogStore 的释放语义：服务容器因「LogStore + ILogStore 两个描述符指向同一实例」会释放**两次**
/// （DI 的已知行为），故 DisposeAsync 必须幂等——否则第二次会在已释放的 CancellationTokenSource 上
/// 抛 ObjectDisposedException，冒泡成未处理异常并让进程 abort（实测 Ctrl+C 关闭 hub 时 core dump）。
/// </summary>
public sealed class LogStoreLifetimeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "openluo-logstore-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task DisposeAsync_可重复调用_不得抛()
    {
        var store = new LogStore(_dir);

        await store.DisposeAsync();
        await store.DisposeAsync();   // 容器会对同一实例再释放一次：第二次必须安全返回
    }

    [Fact]
    public async Task DisposeAsync_先入队再释放_不丢异常()
    {
        var store = new LogStore(_dir);

        // 真实用法：写入侧先入队，容器释放时再收尾
        store.Enqueue(new openLuo.Core.Interfaces.LogRecord(
            Id: 0,
            Ts: DateTimeOffset.Now,
            Level: "info",
            Module: "test",
            Category: "test",
            Source: null,
            Msg: "hello",
            Data: null));

        await store.DisposeAsync();
        await store.DisposeAsync();
    }
}
