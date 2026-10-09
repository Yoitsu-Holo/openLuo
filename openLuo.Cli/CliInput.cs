namespace openLuo.Cli;

/// <summary>CLI 输入解析（纯逻辑，不依赖内核）。</summary>
public static class CliInputParser
{
    public static CliInput Parse(string? raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (!text.StartsWith('/'))
            return new CliInput(CliInputKind.Text, string.Empty, text, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var tokens = Tokenize(text[1..]);
        if (tokens.Count == 0)
            return new CliInput(CliInputKind.Empty, string.Empty, string.Empty, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var args = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens.Skip(1))
        {
            var separator = token.IndexOf('=');
            if (separator > 0)
                options[token[..separator]] = token[(separator + 1)..];
            else
                args.Add(token);
        }
        return new CliInput(CliInputKind.Command, tokens[0], string.Join(' ', tokens.Skip(1)), args, options);
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in value)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}

public enum CliInputKind { Empty, Text, Command }

public sealed record CliInput(
    CliInputKind Kind,
    string Command,
    string Text,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Options);
