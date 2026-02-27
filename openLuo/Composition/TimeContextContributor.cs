using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 宿主级时间上下文贡献（复用旧链 TimeContextFormatter 的 Realtime 格式）。
/// 开关来自 agent.jsonc 的 injectTimeContext（静态 Config 已由 Bootstrapper 初始化）。
/// </summary>
public sealed class TimeContextContributor : IContextContributor
{
    public string Id => "host.time";

    public Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default)
    {
        var inject = openLuo.Infrastructure.Config.Config.Agent.InjectTimeContext;
        if (!inject)
            return Task.FromResult(new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Unavailable, Reason = "injectTimeContext disabled", Retryable = false }
            });

        var now = DateTimeOffset.Now;
        var content = $"[{now:yyyy:MM:dd:HH:mm:ss}]";
        return Task.FromResult(new ContextContributionResult
        {
            State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
            Contributions =
            [
                new ContextContribution
                {
                    Id = "time", ContributorId = Id, Region = ContextRegion.TimeContext,
                    Content = content, Priority = 0, TokenEstimate = 4
                }
            ]
        });
    }
}
