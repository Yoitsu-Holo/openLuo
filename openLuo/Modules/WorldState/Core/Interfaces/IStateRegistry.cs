using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Manages in-memory State definitions registered by plugins.</summary>
public interface IStateRegistry
{
    /// <summary>Register (or update) a state definition.</summary>
    void Register(StateDef def);

    /// <summary>Get a definition by namespace + ownerKind + key.</summary>
    StateDef? GetDef(string @namespace, StateOwnerKind ownerKind, string key);

    /// <summary>Get all registered definitions.</summary>
    IEnumerable<StateDef> GetAllDefs();

    /// <summary>Get all definitions for a given namespace.</summary>
    IEnumerable<StateDef> GetDefsByNamespace(string @namespace);
}
