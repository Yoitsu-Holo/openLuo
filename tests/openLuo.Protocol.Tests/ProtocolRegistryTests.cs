using System.Reflection;
using openLuo.Protocol;
using Xunit;

namespace openLuo.Protocol.Tests;

/// <summary>协议注册表必须覆盖全部已声明常量（防「新增常量却漏登记」的漂移）。</summary>
public sealed class ProtocolRegistryTests
{
    [Fact]
    public void Registry_CoversAllDeclaredCommands()
    {
        var declared = DeclaredConstants(typeof(MessageTypes));
        var missing = declared.Except(ProtocolRegistry.Commands, StringComparer.Ordinal).ToList();

        Assert.Empty(missing);
        Assert.Equal(declared.Count, ProtocolRegistry.Commands.Count);
    }

    [Fact]
    public void Registry_CoversAllDeclaredEvents()
    {
        var declared = DeclaredConstants(typeof(EventTypes));
        var missing = declared.Except(ProtocolRegistry.Events, StringComparer.Ordinal).ToList();

        Assert.Empty(missing);
        Assert.Equal(declared.Count, ProtocolRegistry.Events.Count);
    }

    [Fact]
    public void Registry_ClassifiesTypes()
    {
        Assert.True(ProtocolRegistry.IsCommand(MessageTypes.TurnSubmit));
        Assert.True(ProtocolRegistry.IsEvent(EventTypes.Output));
        Assert.False(ProtocolRegistry.IsCommand(EventTypes.Output));
        Assert.False(ProtocolRegistry.IsKnown("does.not.exist"));
    }

    private static List<string> DeclaredConstants(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}
