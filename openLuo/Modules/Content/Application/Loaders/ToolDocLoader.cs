using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Loaders;

public static class ToolDocLoader
{
    public static IReadOnlyList<ToolDefinition> LoadAll(string baseDir)
    {
        var dir = Path.Combine(baseDir, "data", "tools");
        if (!Directory.Exists(dir))
            return [];

        var definitions = new List<ToolDefinition>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var body = File.ReadAllText(file);
            definitions.Add(new ToolDefinition
            {
                Id = Path.GetFileNameWithoutExtension(file),
                DisplayName = ReadTitle(file, body),
                Description = ReadSummary(body),
                EntryPoint = file,
                CapabilityTags = ["doc-asset"],
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source_kind"] = "tool_doc"
                }
            });
        }

        return definitions;
    }

    private static string ReadTitle(string path, string body)
    {
        var heading = body.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# "));
        return string.IsNullOrWhiteSpace(heading)
            ? Path.GetFileNameWithoutExtension(path)
            : heading[2..].Trim();
    }

    private static string ReadSummary(string body) =>
        body.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
        ?? string.Empty;
}
