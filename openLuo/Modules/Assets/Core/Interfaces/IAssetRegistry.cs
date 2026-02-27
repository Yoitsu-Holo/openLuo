using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Core.Interfaces;

/// <summary>
/// In-memory registry for asset type definitions registered by plugins.
/// Analogous to IStateRegistry — provides a catalog of known asset types.
/// </summary>
public interface IAssetRegistry
{
    /// <summary>Register an asset type definition. Overwrites if definition ID already exists.</summary>
    void Register(AssetDef def);

    /// <summary>Get an asset definition by type and namespace, or null if not registered.</summary>
    AssetDef? GetDef(string assetType, string @namespace);

    /// <summary>Get all registered asset definitions.</summary>
    IEnumerable<AssetDef> GetAllDefs();
}
