using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Timeline event scheduling and due-event lifecycle service.</summary>
public interface ITimelineService
{
    Task<TimelineEvent> CreateAsync(string gameId, TimelineCreateRequest request, CancellationToken ct = default);

    Task<List<TimelineEvent>> QueryAsync(string gameId, TimelineQueryOptions options, CancellationToken ct = default);

    Task<List<TimelineEvent>> PollDueAsync(string gameId, long nowEpochMs, int limit = 32, CancellationToken ct = default);

    Task<bool> AckAsync(string gameId, string eventId, string finalStatus = TimelineEventStatus.Done, CancellationToken ct = default);

    Task<bool> CancelAsync(string gameId, string eventId, CancellationToken ct = default);
}
