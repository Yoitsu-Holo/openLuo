using System.Text.Json;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Infrastructure.Security;

public class InputValidator(IRuntimeConfigCenter? configCenter = null)
{
    private static readonly string[] SqlKeywords =
    {
        "DROP", "DELETE", "INSERT", "UPDATE", "EXEC", "EXECUTE",
        "SCRIPT", "UNION", "SELECT", "--", "/*", "*/", "ALTER",
        "CREATE", "TRUNCATE", "GRANT", "REVOKE", "XP_", "SP_"
    };

    private static readonly char[] PromptBreakChars = { '{', '}', '<', '>' };
    private readonly IRuntimeConfigCenter? _configCenter = configCenter;

    public bool ValidateUserInput(string input, int? maxLength = null)
    {
        if (string.IsNullOrEmpty(input)) return false;
        var limit = maxLength ?? Math.Max(1, _configCenter?.GetSnapshot().Security.MaxInputLength ?? 1000);
        if (input.Length > limit) return false;

        var upper = input.ToUpperInvariant();
        if (SqlKeywords.Any(keyword => upper.Contains(keyword))) return false;

        var breakCharCount = input.Count(c => PromptBreakChars.Contains(c));
        return breakCharCount <= Math.Max(1, _configCenter?.GetSnapshot().Security.PromptBreakCharLimit ?? 10);
    }


    public bool ValidateResourceId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    public bool ValidateJson(string json, out object? result)
    {
        result = null;
        if (string.IsNullOrEmpty(json)) return false;

        try
        {
            result = JsonSerializer.Deserialize<object>(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
