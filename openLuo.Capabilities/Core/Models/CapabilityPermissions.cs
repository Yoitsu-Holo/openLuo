namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 会话权限（D7 策略）。决定 Agent 可见与可调用的能力范围。
/// </summary>
public sealed class CapabilityPermissions
{
    public IReadOnlySet<string> AllowedCanonicalIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> AllowedKinds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        nameof(CapabilityKind.Builtin),
        nameof(CapabilityKind.Mcp),
        nameof(CapabilityKind.Workflow),
        nameof(CapabilityKind.RemoteAgent)
    };
    public bool AllowMutation { get; init; } = true;
    public bool AllowExternal { get; init; } = true;
    public bool AllowDelegation { get; init; } = true;

    public static CapabilityPermissions AllowAll { get; } = new();
}
