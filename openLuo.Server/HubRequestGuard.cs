using openLuo.Server.Security;

namespace openLuo.Server;

/// <summary>
/// Hub 入口治理（hub-protocol §4.9）：把 <see cref="HubSecurityPolicy"/> 变成实际拦截——
/// HTTP 请求限流、WS 命令限流、WS 帧上限、资源 id 与回合文本校验。
/// </summary>
/// <remarks>
/// 策略经 <see cref="HubServerOptions.Security"/> 惰性刷新（每 30 次访问检查一次，支持热加载）；
/// 策略值变化时重建限流器与校验器。全程 <c>Enabled=false</c> 时直接放行。
/// </remarks>
internal sealed class HubRequestGuard
{
    private readonly Func<HubSecurityPolicy>? _provider;
    private readonly object _gate = new();
    private HubSecurityPolicy _policy;
    private RateLimiter _limiter;
    private InputValidator _validator;
    private int _checksSinceRefresh;

    public HubRequestGuard(Func<HubSecurityPolicy>? policyProvider)
    {
        _provider = policyProvider;
        _policy = policyProvider?.Invoke() ?? HubSecurityPolicy.Default;
        _limiter = new RateLimiter(_policy.RequestsPerMinute, _policy.BurstLimit);
        _validator = new InputValidator(_policy.MaxInputLength);
    }

    /// <summary>当前策略快照（需要时先刷新）。</summary>
    public HubSecurityPolicy Policy
    {
        get
        {
            Refresh();
            return _policy;
        }
    }

    /// <summary>限流：key 建议用 token（首选）→ 客户端 IP → 连接 id。未启用时放行。</summary>
    public bool Allow(string? key) => !Policy.Enabled || _limiter.Allow(key);

    /// <summary>资源 id 校验（会话 id / 资产 id / 轨迹 id）。未启用时放行。</summary>
    public bool ValidResourceId(string? id) => !Policy.Enabled || _validator.ValidateResourceId(id);

    /// <summary>
    /// 回合文本校验：先做结构检查（**空文本放行**——允许"仅媒体块"的回合；含 NUL 或超长直接拒绝），
    /// 再在严格模式下跑启发式（<see cref="HubSecurityPolicy.StrictInputValidation"/>）。
    /// </summary>
    public bool ValidTurnText(string? text)
    {
        var policy = Policy;
        if (!policy.Enabled || string.IsNullOrEmpty(text))
            return true;
        if (text.Contains('\0'))
            return false;
        if (text.Length > Math.Max(1, policy.MaxInputLength))
            return false;
        return !policy.StrictInputValidation || _validator.ValidateUserInput(text, policy.MaxInputLength);
    }

    /// <summary>帧/命令字节上限（超限回 <c>2002</c> 并断开）。</summary>
    public int MaxFrameBytes => Math.Max(1024, Policy.MaxFrameBytes);

    /// <summary>限流提示（统一措辞，供错误消息使用）。</summary>
    public string RateLimitHint
    {
        get
        {
            var policy = Policy;
            return $"rate limited: at most {policy.RequestsPerMinute}/min, {policy.BurstLimit}/10s";
        }
    }

    /// <summary>惰性刷新策略（每 30 次访问检查一次，避免每请求都读配置中心）。</summary>
    private void Refresh()
    {
        if (_provider is null)
            return;

        if (Interlocked.Increment(ref _checksSinceRefresh) % 30 != 0)
            return;

        var next = _provider() ?? HubSecurityPolicy.Default;
        lock (_gate)
        {
            if (next == _policy)
                return;
            _policy = next;
            _limiter = new RateLimiter(next.RequestsPerMinute, next.BurstLimit);
            _validator = new InputValidator(next.MaxInputLength);
        }
    }
}
