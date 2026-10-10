namespace openLuo.Core.Interfaces;

/// <summary>已签发令牌（持久化用）。</summary>
public sealed record TokenRecord(string Token, string ClientId, string Role, DateTimeOffset ExpiresAt);

/// <summary>
/// 令牌持久化端口：Hub 重启后已发令牌仍然有效（§4.4）。
/// 未接入实现时令牌仅存在于进程内。
/// </summary>
public interface ITokenStore
{
    void Save(TokenRecord record);

    void Remove(string token);

    /// <summary>载入全部令牌（实现可过滤已过期项）。</summary>
    IReadOnlyList<TokenRecord> LoadAll();
}
