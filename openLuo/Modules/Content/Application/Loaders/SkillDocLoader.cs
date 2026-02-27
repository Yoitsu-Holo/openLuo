using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application.Loaders;

public static class SkillDocLoader
{
    public static IReadOnlyList<SkillDefinition> LoadAll(string baseDir)
    {
        var dir = Path.Combine(baseDir, "data", "skills");
        if (!Directory.Exists(dir))
            return [];

        var definitions = new List<SkillDefinition>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var body = File.ReadAllText(file);
            definitions.Add(new SkillDefinition
            {
                Id = Path.GetFileNameWithoutExtension(file),
                DisplayName = ReadTitle(file, body),
                Description = ReadSummary(body),
                Category = "doc",
                Usage = "documentation asset",
                Body = body.Trim(),
                CapabilityTags = ["doc-asset"],
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source_kind"] = "skill_doc"
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
