using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;

namespace openLuo.Composition;

/// <summary>
/// 平台语境注入（全局管线的一部分）：
/// 从 ContextBuildRequest.Extras 读取平台元数据（scene/sender/channel），
/// 动态加载——仅当平台透传了语境数据时注入 [Platform] 块；CLI/TUI 无数据则零注入。
/// </summary>
public sealed class PlatformContextContributor : IContextContributor
{
    public string Id => "host.platform";

    public Task<ContextContributionResult> ContributeAsync(
        ContextBuildRequest request,
        CancellationToken ct = default)
    {
        var extras = request.Extras;
        var scene = extras.TryGetValue("scene", out var s) ? s?.ToString() : null;
        if (string.IsNullOrWhiteSpace(scene))
            return NoPlatform();

        var parts = new List<string> { $"scene: {scene}" };
        if (extras.TryGetValue("senderName", out var sender) && !string.IsNullOrWhiteSpace(sender?.ToString()))
            parts.Add($"current sender: {sender}");
        if (extras.TryGetValue("channelId", out var channel) && !string.IsNullOrWhiteSpace(channel?.ToString()))
            parts.Add($"channel: {channel}");
        if (extras.TryGetValue("groupName", out var group) && !string.IsNullOrWhiteSpace(group?.ToString()))
            parts.Add($"group: {group}");

        var content = $"[Platform] {string.Join("; ", parts)}";
        return Task.FromResult(new ContextContributionResult
        {
            State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
            Contributions =
            [
                new ContextContribution
                {
                    Id = "platform", ContributorId = Id, Region = ContextRegion.RuntimeRules,
                    Content = content, Priority = 90, TokenEstimate = Math.Max(1, content.Length / 4)
                }
            ]
        });
    }

    private static Task<ContextContributionResult> NoPlatform() => Task.FromResult(
        new ContextContributionResult
        {
            State = new ContextSourceState
            {
                SourceId = "host.platform", Status = ContextSourceStatus.Unavailable,
                Reason = "no platform metadata", Retryable = false
            }
        });
}
