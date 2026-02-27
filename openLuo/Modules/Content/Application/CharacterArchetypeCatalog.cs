using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application;

public sealed class CharacterArchetypeCatalog
{
    private readonly IReadOnlyDictionary<string, CharacterArchetypeDefinition> _byId;

    public CharacterArchetypeCatalog(ContentRegistry contentRegistry)
    {
        _byId = contentRegistry.GetAll<CharacterArchetypeDefinition>()
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CharacterArchetypeDefinition> All => _byId.Values.ToList();

    public CharacterArchetypeDefinition? GetById(string id) =>
        _byId.TryGetValue(id.Trim(), out var archetype)
            ? archetype
            : null;
}
