using openLuo.AgentContext.Core;

namespace openLuo.AgentContext.Infrastructure;

/// <summary>
/// 默认 token 估算（D44）：无分词器依赖的启发式。
/// <para>
/// 规则：CJK / 假名 / 谚文 / 全角字符按 **1 字 ≈ 1 token**；其余（拉丁字母、数字、空白、ASCII 标点）
/// 按 **4 字符 ≈ 1 token** 且向上取整。混排时两部分相加。
/// </para>
/// <para>
/// 为什么不是统一 <c>Length / 4</c>：那是纯 ASCII 的经验值，对中文**低估约 4 倍**
/// （1000 个汉字的真实用量接近 1000 token，而非 250），会让预算形同虚设。
/// 该启发式仍只是估算：真值由宿主注入的 <see cref="ITokenCounter"/>（与模型配套的分词器）决定。
/// </para>
/// </summary>
public sealed class HeuristicTokenCounter : ITokenCounter
{
    /// <summary>无状态单例（实现无字段）。</summary>
    public static HeuristicTokenCounter Instance { get; } = new();

    public int Count(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int wide = 0, narrow = 0;
        foreach (var ch in text)
        {
            if (IsWide(ch))
                wide++;
            else
                narrow++;
        }

        // 窄字符每 4 个约 1 token；向上取整，避免长串被系统性低估。
        return wide + ((narrow + 3) / 4);
    }

    /// <summary>全角宽度字符：中日韩统一表意（含扩展/兼容）、假名、谚文、全角标点与全角拉丁、代理对。</summary>
    private static bool IsWide(char ch) => ch switch
    {
        >= '\u1100' and <= '\u115F' => true,   // Hangul Jamo
        >= '\u2E80' and <= '\uA4CF' => true,   // CJK 部首/康熙/注音/假名/谚文兼容/CJK 统一+扩展A/彝文
        >= '\uAC00' and <= '\uD7A3' => true,   // Hangul 音节
        >= '\uF900' and <= '\uFAFF' => true,   // CJK 兼容表意
        >= '\uFE30' and <= '\uFE4F' => true,   // CJK 兼容形式
        >= '\uFF00' and <= '\uFF60' => true,   // 全角形式
        >= '\uFFE0' and <= '\uFFE6' => true,   // 全角符号（￥￦ 等）
        _ => char.IsSurrogate(ch),             // 扩展 B+ 表意以代理对出现
    };
}
