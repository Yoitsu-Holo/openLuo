using System.Globalization;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Cli;

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
public sealed record CliInput(CliInputKind Kind, string Command, string Text, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Options);

public static class CliRenderer
{
    public static string Render(OutputItem item) => item.Kind switch
    {
        ReplyItemKind.Text => Convert.ToString(item.Payload, CultureInfo.InvariantCulture) ?? string.Empty,
        ReplyItemKind.Image => $"[image] {Convert.ToString(item.Payload, CultureInfo.InvariantCulture)}",
        ReplyItemKind.Audio => $"[audio] {Convert.ToString(item.Payload, CultureInfo.InvariantCulture)}",
        ReplyItemKind.File => $"[file] {Convert.ToString(item.Payload, CultureInfo.InvariantCulture)}",
        // Card = 结构化不透明载荷：CLI 无渲染语义，按契约降级——优先可点 Url，其次紧凑 JSON。
        ReplyItemKind.Card => RenderCard(item.Payload),
        _ => Convert.ToString(item.Payload, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static string RenderCard(object? payload)
    {
        if (payload is null)
            return "[card]";
        try
        {
            var root = System.Text.Json.JsonSerializer.SerializeToElement(payload);
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                && root.TryGetProperty("Url", out var url)
                && url.ValueKind == System.Text.Json.JsonValueKind.String
                && !string.IsNullOrWhiteSpace(url.GetString()))
                return url.GetString()!;
            return $"[card] {System.Text.Json.JsonSerializer.Serialize(payload)}";
        }
        catch
        {
            return $"[card] {payload}";
        }
    }
}

public sealed class CliOutputSubscriber
{
    private readonly IOutputQueue _queue;
    public CliOutputSubscriber(IOutputQueue queue) => _queue = queue;

    public async Task ConsumeAsync(Func<string, Task> sendAsync, CancellationToken ct = default)
    {
        await foreach (var item in _queue.ReadAsync(ct))
        {
            try
            {
                await sendAsync(CliRenderer.Render(item));
                await _queue.AckAsync(item.Sequence, ct);
            }
            catch when (!ct.IsCancellationRequested)
            {
                await _queue.FailAsync(item.Sequence, permanent: true, ct);
                await sendAsync("[output delivery failed]");
            }
        }
    }
}
