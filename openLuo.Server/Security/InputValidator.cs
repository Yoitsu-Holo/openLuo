using System.Text.Json;

namespace openLuo.Server.Security;

/// <summary>
/// 输入校验（原 <c>openLuo.Infrastructure.Security.InputValidator</c> 迁入 Server 并改为纯值构造）：
/// 资源 id 白名单、JSON 合法性、以及可选的"用户输入启发式"。
/// </summary>
/// <remarks>
/// 启发式（SQL 关键字 + 提示词破坏字符）**默认不用于聊天文本**——它会对正常对话产生误判，
/// 仅在 <see cref="HubSecurityPolicy.StrictInputValidation"/> 打开时启用（见 hub-protocol §4.9）。
/// </remarks>
public sealed class InputValidator(int maxInputLength = 32_768, int promptBreakCharLimit = 10)
{
    private static readonly string[] SqlKeywords =
    {
        "DROP", "DELETE", "INSERT", "UPDATE", "EXEC", "EXECUTE",
        "SCRIPT", "UNION", "SELECT", "--", "/*", "*/", "ALTER",
        "CREATE", "TRUNCATE", "GRANT", "REVOKE", "XP_", "SP_"
    };

    private static readonly char[] PromptBreakChars = { '{', '}', '<', '>' };

    private readonly int _maxInputLength = Math.Max(1, maxInputLength);
    private readonly int _promptBreakCharLimit = Math.Max(1, promptBreakCharLimit);

    /// <summary>启发式用户输入校验：长度上限 + SQL 关键字黑名单 + 提示词破坏字符计数。</summary>
    public bool ValidateUserInput(string? input, int? maxLength = null)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        if (input.Length > (maxLength ?? _maxInputLength))
            return false;

        var upper = input.ToUpperInvariant();
        if (SqlKeywords.Any(keyword => upper.Contains(keyword, StringComparison.Ordinal)))
            return false;

        return input.Count(c => PromptBreakChars.Contains(c)) <= _promptBreakCharLimit;
    }

    /// <summary>资源 id：仅允许字母 / 数字 / <c>-</c> / <c>_</c>（挡掉路径穿越与分隔符注入）。</summary>
    public bool ValidateResourceId(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    /// <summary>JSON 合法性校验。</summary>
    public bool ValidateJson(string? json, out object? result)
    {
        result = null;
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            result = JsonSerializer.Deserialize<object>(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
