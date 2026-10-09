using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Core.Models;
using openLuo.Modules.Agent.Application;

namespace openLuo.Modules.Agent.Infrastructure;

/// <summary>
/// 基于 archetype 数据目录的 roster 实现：扫描 data/archetypes/*.jsonc。
/// </summary>
public sealed class ArchetypeAgentRoster : IAgentRoster
{
    private readonly string _archetypesDir;

    public ArchetypeAgentRoster(string baseDir)
    {
        _archetypesDir = Path.Combine(baseDir, "data", "archetypes");
    }

    public Task<IReadOnlyList<Character>> ListAsync(string gameId, CancellationToken ct = default)
    {
        var result = new List<Character>();
        if (!Directory.Exists(_archetypesDir))
            return Task.FromResult<IReadOnlyList<Character>>(result);
        foreach (var file in Directory.EnumerateFiles(_archetypesDir, "*.jsonc"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var node = JsonNode.Parse(json);
                var id = node?["id"]?.GetValue<string>();
                var name = node?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                result.Add(new Character { Id = id, Name = name ?? id });
            }
            catch
            {
                // 单文件损坏不影响 roster
            }
        }
        return Task.FromResult<IReadOnlyList<Character>>(result);
    }

    public Task<Character?> ResolveAsync(string gameId, string selector, CancellationToken ct = default) =>
        Task.FromResult(Resolve(selector));

    private Character? Resolve(string selector)
    {
        var normalized = selector.Trim();
        foreach (var character in ListAsync(string.Empty).GetAwaiter().GetResult())
        {
            if (string.Equals(character.Id, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(character.Name, normalized, StringComparison.OrdinalIgnoreCase))
                return character;
        }
        return null;
    }
}
