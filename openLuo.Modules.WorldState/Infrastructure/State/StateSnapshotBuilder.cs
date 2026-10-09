using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Infrastructure.State;

public class StateSnapshotBuilder(IStateQueryService queryService) : IStateSnapshotBuilder
{
    public async Task<StateSnapshot> BuildAsync(string gameId, string? characterId = null)
    {
        var snapshot = new StateSnapshot();

        // Query all game-owned states
        var gameStates = await queryService.QueryAsync(
            gameId,
            @namespace: null,
            ownerKind: StateOwnerKind.Game,
            ownerId: "global",
            keys: null,
            includeDefaults: true);

        foreach (var item in gameStates)
            RouteIntoSnapshot(snapshot, item);

        // If a character is specified, query character-owned states
        if (characterId is not null)
        {
            var charStates = await queryService.QueryAsync(
                gameId,
                @namespace: null,
                ownerKind: StateOwnerKind.Character,
                ownerId: characterId,
                keys: null,
                includeDefaults: true);

            foreach (var item in charStates)
                RouteIntoSnapshot(snapshot, item);
        }

        return snapshot;
    }

    private static void RouteIntoSnapshot(StateSnapshot snapshot, StateValue item)
    {
        switch (item.Namespace.ToLowerInvariant())
        {
            case "char_status":
                snapshot.CharStatus[item.Key] = item.Value;
                break;
            case "world_state":
                snapshot.WorldState[item.Key] = item.Value;
                break;
            case "game_resource":
                snapshot.GameResource[item.Key] = item.Value;
                break;
            case "scene_state":
                snapshot.SceneState[item.Key] = item.Value;
                break;
            default:
                if (!snapshot.Extra.TryGetValue(item.Namespace, out var subDict))
                {
                    subDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    snapshot.Extra[item.Namespace] = subDict;
                }
                subDict[item.Key] = item.Value;
                break;
        }
    }
}
