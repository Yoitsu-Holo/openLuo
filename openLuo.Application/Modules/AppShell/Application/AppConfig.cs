using openLuo.Modules.Embedding.Core.Models;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.AppShell.Application;

public class LogConfig
{
    /// <summary>off / error / warn / info / debug</summary>
    public string Level { get; set; } = "info";
    public bool OutputToConsole { get; set; } = false;
    public Dictionary<string, string> Categories { get; set; } = new();

    /// <summary>热库（近期日志 SQL 热查询 + 保留裁剪）。</summary>
    public LogHotConfig Hot { get; set; } = new();

    /// <summary>冷文件（全量 JSONL 落盘，按天分目录）。</summary>
    public LogArchiveConfig Archive { get; set; } = new();

    public LogConfig Clone() => new()
    {
        Level = Level,
        OutputToConsole = OutputToConsole,
        Categories = Categories is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(Categories, StringComparer.OrdinalIgnoreCase),
        Hot = Hot?.Clone() ?? new LogHotConfig(),
        Archive = Archive?.Clone() ?? new LogArchiveConfig()
    };
}

/// <summary>热库配置：近期日志入 SQLite，支持 SQL/全文热查询；超窗裁剪。</summary>
public class LogHotConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>库文件相对路径（相对日志目录），默认 <c>hot.db</c>。</summary>
    public string Path { get; set; } = "hot.db";

    /// <summary>保留天数。</summary>
    public int RetainDays { get; set; } = 14;

    /// <summary>最大保留行数（超出删最旧）。</summary>
    public int MaxRows { get; set; } = 200_000;

    /// <summary>批量写入条数上限。</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>不足一批时的等待毫秒（合并写）。</summary>
    public int FlushMs { get; set; } = 250;

    public LogHotConfig Clone() => new()
    {
        Enabled = Enabled, Path = Path, RetainDays = RetainDays,
        MaxRows = MaxRows, BatchSize = BatchSize, FlushMs = FlushMs
    };
}

/// <summary>冷文件配置：全量日志按天分目录落盘（默认永久保留，不删）。</summary>
public class LogArchiveConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>根目录相对路径（相对日志目录），默认 <c>core</c>。</summary>
    public string Dir { get; set; } = "core";

    public LogArchiveConfig Clone() => new() { Enabled = Enabled, Dir = Dir };
}

public class AgentRuntimeConfig
{
    /// <summary>
    /// 回合总时限（秒）→ 内核 <c>DecisionBudgets.OverallDeadline</c>。
    /// 注意语义：这是**两次决策之间**的时钟比较，不是 CancelAfter——已经开始的工具调用不会被它打断
    /// （单工具超时由 <c>DecisionBudgets.StepIdleTimeout</c> 负责）。默认 600s，因为慢工具链路
    /// （远程 TTS 实测约 150s + 后续决策）需要余量。
    /// </summary>
    public int ChatRoundTimeoutSeconds { get; set; } = 600;

    /// <summary>Hub 高危能力确认握手超时（秒）→ <c>HubServerOptions.ConfirmTimeoutSeconds</c>。</summary>
    public int PendingAbilityConfirmTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// 单回合从对话存储抓取的最大历史条数（**抓取上限**，真正的裁剪由
    /// <see cref="ContextTokenBudget"/> 的剩余预算决定，从最旧一侧截断）。默认 24。
    /// </summary>
    public int ContextConversationRetainCount { get; set; } = 24;

    /// <summary>
    /// 上下文 token 预算上限（系统块 + 对话历史；估算口径见内核 <c>ITokenCounter</c>，
    /// 默认启发式：CJK 1 字 ≈ 1 token、其余 4 字符 ≈ 1 token）。默认 24000。
    /// <para>
    /// 超出预算时：保底区域（人格 / 时间 / 平台规则 / 能力说明 / 当轮输入 / 工具结果）仍然保留，
    /// 可裁区域（记忆 / 场景 / 世界 / 历史）按优先级让位；被裁明细记录在快照
    /// <c>Budget.DroppedContributions</c>，保底集自身超支时标记 <c>Budget.OverBudget</c>。
    /// </para>
    /// </summary>
    public int ContextTokenBudget { get; set; } = 24_000;

    /// <summary>
    /// 是否向每个对话上下文注入当前时间标记（内核能力，默认开启）。
    /// 注入方案由内核 TimeMode 决定（Realtime → 绝对时间，Virtual → 相对时间，Disabled → 不注入），
    /// 不暴露给 bot 配置。
    /// </summary>
    public bool InjectTimeContext { get; set; } = true;

    public AgentRuntimeConfig Clone() => new()
    {
        ChatRoundTimeoutSeconds = ChatRoundTimeoutSeconds,
        PendingAbilityConfirmTimeoutSeconds = PendingAbilityConfirmTimeoutSeconds,
        ContextConversationRetainCount = ContextConversationRetainCount,
        ContextTokenBudget = ContextTokenBudget,
        InjectTimeContext = InjectTimeContext
    };
}

public class SecurityRuntimeConfig
{
    // ── Hub 入口治理（hub-protocol §4.9）：限流 + 输入校验，默认宽松但真实生效 ──
    // 说明：早期的 RateLimitPerMinute/MaxInputLength/BurstLimit/PromptBreakCharLimit 与
    // 图像尺寸/下载相关键均为**零消费**的假旋钮（实现早已迁到 HubSecurityPolicy + 平台侧），已删除。

    /// <summary>Hub 入口治理总开关。</summary>
    public bool HubEnabled { get; set; } = true;

    /// <summary>单个 key（token / 客户端 IP / 连接）每分钟请求上限。</summary>
    public int HubRequestsPerMinute { get; set; } = 600;

    /// <summary>单个 key 每 10 秒突发上限。</summary>
    public int HubBurstLimit { get; set; } = 60;

    /// <summary>回合文本硬上限（字符）；正常聊天远达不到，用于挡误用与滥用。</summary>
    public int HubMaxInputLength { get; set; } = 32_768;

    /// <summary>单条 WS 文本帧上限（字节）。</summary>
    public int HubMaxFrameBytes { get; set; } = 1_048_576;

    /// <summary>严格模式：回合文本额外过 SQL 关键字/提示词破坏字符启发式（默认关，会对正常聊天误判）。</summary>
    public bool HubStrictInputValidation { get; set; }

    public SecurityRuntimeConfig Clone() => new()
    {
        HubEnabled = HubEnabled,
        HubRequestsPerMinute = HubRequestsPerMinute,
        HubBurstLimit = HubBurstLimit,
        HubMaxInputLength = HubMaxInputLength,
        HubMaxFrameBytes = HubMaxFrameBytes,
        HubStrictInputValidation = HubStrictInputValidation
    };
}

public class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public EmbeddingConfig Embedding { get; set; } = new();
    public string DatabasePath { get; set; } = string.Empty;
    public LogConfig Log { get; set; } = new();
    public SqliteVecConfig SqliteVec { get; set; } = new();
    public AgentRuntimeConfig Agent { get; set; } = new();
    public SecurityRuntimeConfig Security { get; set; } = new();

    public AppConfig Clone() => new()
    {
        Llm = Llm.Clone(),
        Embedding = Embedding.Clone(),
        DatabasePath = DatabasePath,
        Log = Log.Clone(),
        SqliteVec = SqliteVec.Clone(),
        Agent = Agent.Clone(),
        Security = Security.Clone(),
    };
}
