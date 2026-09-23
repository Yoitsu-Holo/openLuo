using System.Runtime.CompilerServices;
using openLuo.Infrastructure.Logging;

namespace openLuo.Tests;

/// <summary>
/// 测试程序集级初始化：绑定 Null 日志实现，避免依赖执行顺序的
/// "Logger 尚未初始化" 失败（测试类可自行再次 Initialize 覆盖）。
/// </summary>
internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    public static void InitializeLogger() => Logger.Initialize(new NullLogTarget());
}
