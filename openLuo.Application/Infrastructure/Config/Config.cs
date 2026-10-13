using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Embedding.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Infrastructure.Config;

/// <summary>
/// 静态配置 Facade。启动时 Initialize() 后全局可用，无需 DI 注入 IRuntimeConfigCenter。
/// 支持热重载：RuntimeConfigCenter 在文件变化时同步更新 _snapshot。
/// </summary>
public static class Config
{
    private static volatile AppConfig _snapshot = new();

    /// <summary>启动时绑定配置快照。</summary>
    public static void Initialize(AppConfig snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
    }

    /// <summary>测试时释放引用。</summary>
    public static void Reset()
    {
        _snapshot = new AppConfig();
    }

    /// <summary>热重载时由 RuntimeConfigCenter 调用，原子替换快照。</summary>
    internal static void UpdateSnapshot(AppConfig snapshot)
    {
        _snapshot = snapshot;
    }

    // ── 配置段属性（全部指向当前 _snapshot 下的对应段）──
    // 只保留**有真实消费者**的段：Agent（回合/上下文预算）、Security（Hub 入口治理）、
    // SqliteVec（vec0 扩展）、Log、Llm、Embedding。零消费的段已删除（见 AppConfig 注释）。

    public static LlmConfig Llm => _snapshot.Llm;
    public static EmbeddingConfig Embedding => _snapshot.Embedding;
    public static LogConfig Log => _snapshot.Log;
    public static SqliteVecConfig SqliteVec => _snapshot.SqliteVec;
    public static AgentRuntimeConfig Agent => _snapshot.Agent;
    public static SecurityRuntimeConfig Security => _snapshot.Security;

    // ── 标量属性 ──

    public static string DatabasePath => _snapshot.DatabasePath ?? string.Empty;
}
