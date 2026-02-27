using openLuo.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.Gameplay.Application.Services;

/// <summary>
/// Aggregates status display from State registry and plugin hooks.
/// Uses StatusGroup/StatusOrder/HiddenFromStatus from StateDef.
/// </summary>
public sealed class StatusAggregator(IResourceStatusProjectionService resourceStatusProjectionService) : IStatusAggregator
{
    public async Task<StatusData> GetStatusAsync(string gameId, string characterId, string archetypeId)
    {
        var snapshot = await resourceStatusProjectionService.BuildStatusSnapshotAsync(new ResourceStatusQuery
        {
            GameId = gameId,
            CharacterId = characterId,
            ArchetypeId = archetypeId
        });

        return new StatusData
        {
            Items = snapshot.Items.Select(item => new StatusItem
            {
                Id = item.Id,
                Label = item.Label,
                Type = item.Type,
                Value = item.Value,
                Max = item.Max,
                Group = item.Group,
                Order = item.Order,
                Text = item.Text
            }).ToList(),
            AdditionalText = snapshot.AdditionalText
        };
    }
}
