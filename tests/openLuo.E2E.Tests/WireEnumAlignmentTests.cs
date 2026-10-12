using System.Reflection;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Protocol;
using openLuo.Server;
using Xunit;

namespace openLuo.E2E.Tests;

/// <summary>
/// 内核枚举 ↔ Protocol wire 常量的**一致性守卫**（wire 契约的唯一来源是 `openLuo.Protocol`）。
/// 目的：内核枚举改名 / 增删成员时**立刻失败**，而不是让 wire 值静默漂移（客户端按字符串判定成败，
/// 例如 `tool.result.status`）；同时防止 Protocol 侧留下内核已删成员的死常量。
/// 覆盖六组：能力种类 / 副作用 / 调用状态 / 风险 / 终止原因 / 投递状态。
/// </summary>
public sealed class WireEnumAlignmentTests
{
    private static readonly (Type Kernel, Type Wire)[] Pairs =
    [
        (typeof(CapabilityKind), typeof(CapabilityKinds)),
        (typeof(SideEffectClass), typeof(SideEffects)),
        (typeof(CapabilityStatus), typeof(CapabilityStatuses)),
        (typeof(RiskLevel), typeof(RiskLevels)),
        (typeof(TerminationReason), typeof(TerminationReasons)),
    ];

    private static Dictionary<string, string> WireConsts(Type wire) => wire
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void 内核枚举与wire常量_逐成员一一对应_且取值等于camelCase()
    {
        var problems = new List<string>();

        foreach (var (kernel, wire) in Pairs)
        {
            var members = Enum.GetNames(kernel);
            var consts = WireConsts(wire);

            foreach (var name in members)
            {
                if (!consts.TryGetValue(name, out var value))
                {
                    problems.Add($"{kernel.Name}.{name} → {wire.Name} 缺少同名常量");
                    continue;
                }

                var expected = char.ToLowerInvariant(name[0]) + name[1..];
                if (value != expected)
                    problems.Add($"{wire.Name}.{name} = \"{value}\"（期望 camelCase \"{expected}\"）");
            }

            foreach (var orphan in consts.Keys.Except(members, StringComparer.Ordinal))
                problems.Add($"{wire.Name}.{orphan} 在内核 {kernel.Name} 里已无对应成员（死常量）");
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void 能力描述_种类_副作用_风险_映射覆盖全部成员()
    {
        var problems = new List<string>();

        foreach (var kind in Enum.GetValues<CapabilityKind>())
        {
            var dto = WireMapper.ToDto(new CapabilityDescriptor { CanonicalId = "test:x", Kind = kind });
            var expected = WireConsts(typeof(CapabilityKinds))[kind.ToString()];
            if (dto.Kind != expected)
                problems.Add($"Kind={kind} → \"{dto.Kind}\"，期望 \"{expected}\"");
        }

        foreach (var side in Enum.GetValues<SideEffectClass>())
        {
            var dto = WireMapper.ToDto(new CapabilityDescriptor { CanonicalId = "test:x", SideEffect = side });
            var expected = WireConsts(typeof(SideEffects))[side.ToString()];
            if (dto.SideEffect != expected)
                problems.Add($"SideEffect={side} → \"{dto.SideEffect}\"，期望 \"{expected}\"");
        }

        foreach (var risk in Enum.GetValues<RiskLevel>())
        {
            var dto = WireMapper.ToDto(new CapabilityDescriptor { CanonicalId = "test:x", Risk = risk });
            var expected = WireConsts(typeof(RiskLevels))[risk.ToString()];
            if (dto.Risk != expected)
                problems.Add($"Risk={risk} → \"{dto.Risk}\"，期望 \"{expected}\"");
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void 工具结果状态与回合终止原因_映射覆盖全部成员()
    {
        var problems = new List<string>();

        foreach (var status in Enum.GetValues<CapabilityStatus>())
        {
            var evt = WireMapper.ToToolResult("t1", new CapabilityResult
            {
                InvocationId = "i1",
                Success = status == CapabilityStatus.Ok,
                Status = status,
            });
            var expected = WireConsts(typeof(CapabilityStatuses))[status.ToString()];
            if (evt.Status != expected)
                problems.Add($"CapabilityStatus={status} → \"{evt.Status}\"，期望 \"{expected}\"");
        }

        foreach (var reason in Enum.GetValues<TerminationReason>())
        {
            var dto = WireMapper.ToDto(new TurnResult { Success = true, TerminationReason = reason }, "t1");
            var expected = WireConsts(typeof(TerminationReasons))[reason.ToString()];
            if (dto.TerminationReason != expected)
                problems.Add($"TerminationReason={reason} → \"{dto.TerminationReason}\"，期望 \"{expected}\"");
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void 删除类响应_统一走_DeletedResponse_且形状与既有约定一致()
    {
        // Id 为 null 时不得出现 id 字段（ProtocolJson 设了 WhenWritingNull）→ 与 /v1/schedules/{id} 的 {deleted:true} 一致
        var withoutId = ProtocolJson.Serialize(new DeletedResponse());
        Assert.Equal("{\"deleted\":true}", withoutId);

        var withId = ProtocolJson.Serialize(new DeletedResponse { Id = "ast_abc" });
        Assert.Equal("{\"deleted\":true,\"id\":\"ast_abc\"}", withId);
    }
}
