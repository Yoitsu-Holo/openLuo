using openLuo.Modules.PluginRuntime.Core.Interfaces;
using openLuo.Modules.PluginRuntime.Core.Models.HookContexts;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;
using openLuo.Modules.Content.Application.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace openLuo.Modules.WorldState.Infrastructure.Resources;

public sealed class ResourceStatusProjectionService(
    IResourceCatalogService catalog,
    IStateQueryService stateQueryService,
    IServiceProvider serviceProvider,
    ContentRegistry contentRegistry) : IResourceStatusProjectionService
{
    public async Task<ResourceStatusSnapshot> BuildStatusSnapshotAsync(
        ResourceStatusQuery query,
        CancellationToken ct = default)
    {
        var defs = await catalog.ListDefinitionsAsync(new ResourceDefinitionQuery(), ct);

        var stateSnapshot = await BuildStateSnapshotAsync(query.GameId, defs, query.CharacterId);
        var items = BuildDefinitionItems(query.GameId, query.CharacterId, defs, stateSnapshot, query.IncludeHidden);

        if (query.IncludePluginItems)
            items = await MergePluginItemsAsync(query, defs, stateSnapshot, items, ct);

        return new ResourceStatusSnapshot
        {
            GameId = query.GameId,
            CharacterId = query.CharacterId,
            ArchetypeId = query.ArchetypeId,
            Items = items
                .OrderBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AdditionalText = string.Empty
        };
    }

    private async Task<Dictionary<string, Dictionary<string, SnapshotEntry>>> BuildStateSnapshotAsync(
        string gameId,
        IReadOnlyList<ResourceDefinitionView> defs,
        string characterId)
    {
        var snapshot = new Dictionary<string, Dictionary<string, SnapshotEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in defs)
        {
            var ownerId = ResolveOwnerId(gameId, characterId, def.OwnerKind);
            if (string.IsNullOrWhiteSpace(ownerId))
                continue;

            var value = await stateQueryService.GetAsync(gameId, def.Namespace, def.OwnerKind, ownerId, def.Key);
            var nsKey = ToCamelCase(def.Namespace);
            if (!snapshot.TryGetValue(nsKey, out var nsValues))
            {
                nsValues = new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);
                snapshot[nsKey] = nsValues;
            }

            nsValues[def.Key] = new SnapshotEntry(value.Value, value.Defaulted, value.UpdatedAt);
        }

        return snapshot;
    }

    private async Task<List<ResourceStatusItemView>> MergePluginItemsAsync(
        ResourceStatusQuery query,
        IReadOnlyList<ResourceDefinitionView> defs,
        Dictionary<string, Dictionary<string, SnapshotEntry>> stateSnapshot,
        List<ResourceStatusItemView> items,
        CancellationToken ct)
    {
        var hookSnapshot = stateSnapshot.ToDictionary(
            ns => ns.Key,
            ns => ns.Value.ToDictionary(
                item => item.Key,
                item => item.Value.Value,
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var hookInput = new OnStatusQueryInput
        {
            CharacterId = query.CharacterId,
            ArchetypeId = query.ArchetypeId ?? string.Empty,
            AvailableResources = defs.Select(def => def.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StateSnapshot = hookSnapshot,
            PluginConfigs = contentRegistry.GetMergedPluginConfigs(query.CharacterId, query.ArchetypeId)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
        };

        var pluginHost = serviceProvider.GetService<IPluginHost>();
        var pluginItems = pluginHost is null
            ? []
            : await pluginHost.CallStatusQueryHookAsync(hookInput, ct);
        foreach (var pluginItem in pluginItems)
        {
            if (string.IsNullOrWhiteSpace(pluginItem.Id))
                continue;

            var existingIndex = items.FindIndex(item => item.Id.Equals(pluginItem.Id, StringComparison.OrdinalIgnoreCase));
            var merged = new ResourceStatusItemView
            {
                Id = pluginItem.Id,
                Namespace = string.Empty,
                Key = pluginItem.Id,
                OwnerKind = StateOwnerKind.System,
                OwnerId = string.Empty,
                Label = pluginItem.Label,
                Type = string.IsNullOrWhiteSpace(pluginItem.Type) ? "text" : pluginItem.Type,
                Value = pluginItem.Value,
                Max = pluginItem.Max,
                Group = string.IsNullOrWhiteSpace(pluginItem.Group) ? "plugin" : pluginItem.Group,
                Order = pluginItem.Order,
                Text = pluginItem.Text,
                FromPluginHook = true
            };

            if (existingIndex >= 0)
                items[existingIndex] = Merge(items[existingIndex], merged);
            else
                items.Add(merged);
        }

        return items;
    }

    private static List<ResourceStatusItemView> BuildDefinitionItems(
        string gameId,
        string characterId,
        IReadOnlyList<ResourceDefinitionView> defs,
        Dictionary<string, Dictionary<string, SnapshotEntry>> stateSnapshot,
        bool includeHidden)
    {
        var items = new List<ResourceStatusItemView>();
        foreach (var def in defs)
        {
            if (!includeHidden &&
                (def.HiddenFromStatus || def.LifecycleState == ResourceLifecycleState.Hidden))
                continue;

            var ownerId = ResolveOwnerId(gameId, characterId, def.OwnerKind);
            if (string.IsNullOrWhiteSpace(ownerId))
                continue;

            var value = def.DefaultValue ?? string.Empty;
            var defaulted = true;
            string? updatedAt = null;
            if (stateSnapshot.TryGetValue(ToCamelCase(def.Namespace), out var nsValues) &&
                nsValues.TryGetValue(def.Key, out var snapshotValue))
            {
                value = snapshotValue.Value;
                defaulted = snapshotValue.Defaulted;
                updatedAt = snapshotValue.UpdatedAt;
            }

            items.Add(new ResourceStatusItemView
            {
                Id = def.Key,
                Namespace = def.Namespace,
                Key = def.Key,
                OwnerKind = def.OwnerKind,
                OwnerId = ownerId,
                Label = def.Label,
                Type = ResourceViewMapper.ResolveStatusType(def),
                Value = value,
                Max = ResourceViewMapper.NormalizeMaxValue(def),
                Group = string.IsNullOrWhiteSpace(def.StatusGroup) ? "status" : def.StatusGroup!,
                Order = def.StatusOrder,
                Text = ResourceViewMapper.FormatDisplayText(def, value),
                PluginId = def.PluginId,
                Defaulted = defaulted,
                UpdatedAt = updatedAt,
                HiddenFromStatus = def.HiddenFromStatus
            });
        }

        return items;
    }

    private static ResourceStatusItemView Merge(ResourceStatusItemView definitionItem, ResourceStatusItemView pluginItem) =>
        new()
        {
            Id = definitionItem.Id,
            Namespace = definitionItem.Namespace,
            Key = definitionItem.Key,
            OwnerKind = definitionItem.OwnerKind,
            OwnerId = definitionItem.OwnerId,
            Label = string.IsNullOrWhiteSpace(pluginItem.Label) ? definitionItem.Label : pluginItem.Label,
            Type = string.IsNullOrWhiteSpace(pluginItem.Type) ? definitionItem.Type : pluginItem.Type,
            Value = string.IsNullOrWhiteSpace(pluginItem.Value) ? definitionItem.Value : pluginItem.Value,
            Max = pluginItem.Max ?? definitionItem.Max,
            Group = string.IsNullOrWhiteSpace(pluginItem.Group) ? definitionItem.Group : pluginItem.Group,
            Order = pluginItem.Order == 0 ? definitionItem.Order : pluginItem.Order,
            Text = string.IsNullOrWhiteSpace(pluginItem.Text) ? definitionItem.Text : pluginItem.Text,
            PluginId = definitionItem.PluginId,
            Defaulted = definitionItem.Defaulted,
            HiddenFromStatus = definitionItem.HiddenFromStatus,
            UpdatedAt = definitionItem.UpdatedAt,
            FromPluginHook = true
        };

    private sealed record SnapshotEntry(string Value, bool Defaulted, string? UpdatedAt);

    private static string ResolveOwnerId(string gameId, string characterId, StateOwnerKind ownerKind) =>
        ownerKind switch
        {
            StateOwnerKind.Game => gameId,
            StateOwnerKind.Character => characterId,
            StateOwnerKind.System => "system",
            _ => string.Empty
        };

    private static string ToCamelCase(string snakeCase)
    {
        var parts = snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return snakeCase;

        return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
