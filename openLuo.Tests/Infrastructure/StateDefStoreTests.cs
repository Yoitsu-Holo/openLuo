using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.WorldState.Infrastructure.State;

namespace openLuo.Infrastructure.Tests;

public sealed class StateDefStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"openluo_statedef_{Guid.NewGuid():N}.db");

    private string ConnStr => $"Data Source={_dbPath}";

    [Fact]
    public void Upsert_CreatesStateDefsTable_WhenDatabaseIsFresh()
    {
        var store = new StateDefStore(ConnStr);

        store.Upsert(new StateDef
        {
            Namespace = "system_time",
            Key = "mode",
            OwnerKind = StateOwnerKind.System,
            ValueType = StateValueType.Enum,
            DefaultValue = "virtual",
            EnumValues = ["virtual", "realtime", "disabled"]
        });

        var defs = store.LoadAll();
        var def = Assert.Single(defs);
        Assert.Equal("system_time", def.Namespace);
        Assert.Equal("mode", def.Key);
        Assert.Equal("virtual", def.DefaultValue);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
