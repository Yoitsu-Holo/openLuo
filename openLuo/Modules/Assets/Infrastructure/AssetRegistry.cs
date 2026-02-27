using openLuo.Modules.Assets.Core.Interfaces;
using openLuo.Modules.Assets.Core.Models;

namespace openLuo.Modules.Assets.Infrastructure;

public class AssetRegistry : IAssetRegistry
{
    private readonly Dictionary<string, AssetDef> _defs = new();

    public AssetRegistry()
    {
    }

    public AssetRegistry(AssetDefStore defStore)
    {
        var defs = defStore.LoadAll();
        foreach (var def in defs)
            _defs[def.DefinitionId] = def;
    }

    public void Register(AssetDef def)
    {
        _defs[def.DefinitionId] = def;
    }

    public AssetDef? GetDef(string assetType, string @namespace)
    {
        var key = $"{assetType}:{@namespace}";
        return _defs.TryGetValue(key, out var def) ? def : null;
    }

    public IEnumerable<AssetDef> GetAllDefs()
    {
        return _defs.Values;
    }
}
