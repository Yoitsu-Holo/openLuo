namespace openLuo.Protocol;

/// <summary>群/频道成员（wire）。</summary>
public sealed record MemberDto
{
    /// <summary>稳定用户身份（平台用户 id）。</summary>
    public string UserId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
    public string? Avatar { get; init; }

    /// <summary>角色：owner | admin | member | bot。</summary>
    public string? Role { get; init; }

    public DateTimeOffset JoinedAt { get; init; }
}

/// <summary>`member.joined`：成员加入。</summary>
public sealed record MemberJoinedEvent
{
    public string SessionId { get; init; } = string.Empty;
    public MemberDto Member { get; init; } = new();
}

/// <summary>`member.left`：成员离开。</summary>
public sealed record MemberLeftEvent
{
    public string SessionId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
