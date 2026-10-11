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

    public static LlmConfig Llm => _snapshot.Llm;
    public static EmbeddingConfig Embedding => _snapshot.Embedding;
    public static LogConfig Log => _snapshot.Log;
    public static SqliteVecConfig SqliteVec => _snapshot.SqliteVec;
    public static MemoryRetrievalConfig MemoryRetrieval => _snapshot.MemoryRetrieval;
    public static AgentRuntimeConfig Agent => _snapshot.Agent;
    public static InterAgentConfig InterAgent => _snapshot.InterAgent;
    public static PluginRuntimeConfig PluginRuntime => _snapshot.PluginRuntime;
    public static SecurityRuntimeConfig Security => _snapshot.Security;
    public static LifecycleConfig Lifecycle => _snapshot.Lifecycle;
    public static MemoryStoreConfig MemoryStore => _snapshot.MemoryStore;
    public static TimeoutPolicyConfig Timeouts => _snapshot.Timeouts;
    public static ResiliencePolicyConfig Resilience => _snapshot.Resilience;
    public static ExecutorConfigs Executors => _snapshot.Executors;

    // ── 标量属性 ──

    public static string DatabasePath => _snapshot.DatabasePath ?? string.Empty;
}
