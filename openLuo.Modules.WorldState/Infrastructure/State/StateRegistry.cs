using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.State;

public class StateRegistry : IStateRegistry
{
    private readonly Dictionary<string, StateDef> _defs = new(StringComparer.OrdinalIgnoreCase);

    public StateRegistry()
    {
    }

    public StateRegistry(StateDefStore defStore)
    {
        var defs = defStore.LoadAll();
        foreach (var def in defs)
            _defs[def.DefinitionId] = def;
    }

    public void Register(StateDef def)
    {
        _defs[def.DefinitionId] = def;
    }

    public StateDef? GetDef(string @namespace, StateOwnerKind ownerKind, string key)
    {
        var id = $"{@namespace}:{ownerKind.ToString().ToLowerInvariant()}:{key}";
        return _defs.TryGetValue(id, out var def) ? def : null;
    }

    public IEnumerable<StateDef> GetAllDefs() => _defs.Values;

    public IEnumerable<StateDef> GetDefsByNamespace(string @namespace)
    {
        var prefix = @namespace + ":";
        return _defs.Values.Where(d => d.Namespace.Equals(@namespace, StringComparison.OrdinalIgnoreCase));
    }
}
