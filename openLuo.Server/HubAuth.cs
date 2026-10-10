using System.Collections.Concurrent;
using openLuo.Core.Interfaces;
using openLuo.Protocol;

namespace openLuo.Server;

/// <summary>Hub 角色（§4.8 权限矩阵）。</summary>
public static class HubRoles
{
    public const string Admin = "admin";
    public const string Edge = "edge";
    public const string User = "user";

    /// <summary>角色强度（越大权限越高）。</summary>
    public static int Rank(string? role) => role switch
    {
        Admin => 3,
        Edge => 2,
        User => 1,
        _ => 0,
    };
}

/// <summary>Hub 鉴权配置（§4.4）。</summary>
public sealed class HubAuthOptions
{
    /// <summary>是否允许匿名（开发默认 true；生产应设 false）。</summary>
    public bool AllowAnonymous { get; init; } = true;

    /// <summary>匿名访问所属角色。</summary>
    public string AnonymousRole { get; init; } = HubRoles.User;

    /// <summary>共享密钥：以 <c>secret</c> 换 token 时授予 admin（引导用）。</summary>
    public string? SharedSecret { get; init; }

    /// <summary>API key → 角色。</summary>
    public IReadOnlyDictionary<string, string> ApiKeys { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>token 有效期（分钟）。</summary>
    public int TokenTtlMinutes { get; init; } = 720;
}

/// <summary>内存令牌注册表：签发（校验凭据 → 角色）与解析（token → 角色）。</summary>
public sealed class TokenRegistry
{
    private sealed record Grant(string ClientId, string Role, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Grant> _tokens = new(StringComparer.Ordinal);
    private readonly HubAuthOptions _options;
    private readonly ITokenStore? _store;

    public TokenRegistry(HubAuthOptions options, ITokenStore? store = null)
    {
        _options = options;
        _store = store;

        if (store is null)
            return;

        // 重启恢复：载入未过期令牌
        foreach (var record in store.LoadAll())
            _tokens[record.Token] = new Grant(record.ClientId, record.Role, record.ExpiresAt);
    }

    /// <summary>签发 token；凭据不合法返回 false（附错误码与消息）。</summary>
    public bool TryIssue(TokenRequest request, out TokenResponse response, out int errorCode, out string error)
    {
        var role = ResolveRole(request);
        if (role is null)
        {
            response = new TokenResponse();
            errorCode = ErrorCodes.AuthUnauthorized;
            error = "invalid credentials";
            return false;
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.TokenTtlMinutes));
        var token = "olt_" + ProtocolIds.NewUlid();
        _tokens[token] = new Grant(request.ClientId, role, expiresAt);
        _store?.Save(new TokenRecord(token, request.ClientId, role, expiresAt));

        response = new TokenResponse { Token = token, ExpiresAt = expiresAt, Role = role };
        errorCode = ErrorCodes.Success;
        error = string.Empty;
        return true;
    }

    private string? ResolveRole(TokenRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ApiKey) && _options.ApiKeys.TryGetValue(request.ApiKey, out var mapped))
            return mapped;

        if (!string.IsNullOrWhiteSpace(request.Secret)
            && !string.IsNullOrWhiteSpace(_options.SharedSecret)
            && string.Equals(request.Secret, _options.SharedSecret, StringComparison.Ordinal))
            return HubRoles.Admin;

        return _options.AllowAnonymous ? _options.AnonymousRole : null;
    }

    /// <summary>解析 Bearer token → 角色；缺失/过期/未知返回 null。</summary>
    public string? Resolve(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_tokens.TryGetValue(token, out var grant))
            return null;

        if (grant.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            _store?.Remove(token);
            return null;
        }
        return grant.Role;
    }

    /// <summary>从 Authorization 头解析 token。</summary>
    public static string? BearerOf(string? authorizationHeader) =>
        authorizationHeader is not null && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader["Bearer ".Length..].Trim()
            : null;

    /// <summary>连接级鉴权：返回实际角色；不满足策略返回 null。</summary>
    public string? Authorize(string? bearerToken)
    {
        var role = Resolve(bearerToken);
        if (role is not null)
            return role;
        return _options.AllowAnonymous ? _options.AnonymousRole : null;
    }
}
