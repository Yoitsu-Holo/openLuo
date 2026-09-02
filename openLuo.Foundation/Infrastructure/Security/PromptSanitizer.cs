using System.Text.RegularExpressions;

namespace openLuo.Infrastructure.Security;

public static class PromptSanitizer
{
    private static readonly string[] InjectionPatterns =
    {
        "ignore", "disregard", "forget", "system:", "assistant:", "user:",
        "override", "bypass", "jailbreak", "prompt:",
        "你现在是", "忽略之前", "请忽略", "ignore previous", "new instructions",
        "forget previous", "role:", "act as", "pretend you are"
    };

    public static string SanitizeForPrompt(string userInput)
    {
        if (string.IsNullOrEmpty(userInput)) return string.Empty;

        return userInput
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public static bool DetectInjectionAttempts(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        var lower = input.ToLowerInvariant();
        return InjectionPatterns.Any(pattern => lower.Contains(pattern));
    }
}
