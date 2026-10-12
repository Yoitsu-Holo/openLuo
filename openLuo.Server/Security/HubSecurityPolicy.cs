namespace openLuo.Server.Security;

/// <summary>
/// Hub 入口治理策略（限流 + 输入校验，见 docs/architecture/hub-protocol.md §4.9）。
/// 由**应用层**从 <c>security.jsonc</c> 读出后注入（<see cref="HubServerOptions.Security"/>），
/// Server 自己不读配置文件——维持"服务端只依赖 Protocol + Capabilities"的边界（layers.md §2 B3）。
/// </summary>
/// <param name="Enabled">总开关。false = 不拦截（长度/帧上限也随之失效）。</param>
/// <param name="RequestsPerMinute">单个 key（token / 客户端 IP / 连接）每分钟允许的请求数。</param>
/// <param name="BurstLimit">单个 key 每 10 秒允许的突发数（防瞬间打满）。</param>
/// <param name="MaxInputLength">回合文本的硬上限（字符）；防误用与滥用，正常聊天远达不到。</param>
/// <param name="MaxFrameBytes">单条 WS 文本帧 / 命令的字节上限。</param>
/// <param name="StrictInputValidation">
/// 严格模式：回合文本额外过 SQL 关键字与提示词破坏字符启发式。
/// **默认关闭**——启发式会对正常聊天产生误判（例如聊到"SELECT"），仅在对抗性接入场景打开。
/// </param>
public sealed record HubSecurityPolicy(
    bool Enabled = true,
    int RequestsPerMinute = 600,
    int BurstLimit = 60,
    int MaxInputLength = 32_768,
    int MaxFrameBytes = 1_048_576,
    bool StrictInputValidation = false)
{
    /// <summary>默认策略：宽松但真实生效（见各字段默认值）。</summary>
    public static HubSecurityPolicy Default { get; } = new();
}
