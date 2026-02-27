using openLuo.Modules.Content.Application.Registry;
using openLuo.Modules.Content.Core.Definitions;

namespace openLuo.Modules.Content.Application;

public sealed class ItemDefinitionCatalog
{
    public static readonly IReadOnlyList<(string Category, string CategoryName)> Categories =
    [
        ("clothing", "服饰"),
        ("gift",     "礼品"),
        ("book",     "书籍"),
        ("food",     "食品"),
        ("special",  "特殊道具"),
    ];

    private readonly IReadOnlyList<ItemDefinition> _items;
    private readonly IReadOnlyDictionary<string, ItemDefinition> _byId;

    public ItemDefinitionCatalog(ContentRegistry contentRegistry)
    {
        _items = contentRegistry.GetAll<ItemDefinition>();
        _byId = _items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ItemDefinition> AllItems => _items;

    public IReadOnlyList<ItemDefinition> GetByCategory(string category) =>
        _items.Where(i => i.Tags.Contains(category, StringComparer.OrdinalIgnoreCase)).ToList();

    public ItemDefinition? GetById(string id) =>
        _byId.TryGetValue(id.Trim(), out var item)
            ? item
            : null;

    public ItemDefinition? FindByReference(string itemRef)
    {
        if (string.IsNullOrWhiteSpace(itemRef))
            return null;

        var normalized = itemRef.Trim();
        return _items.FirstOrDefault(item =>
            string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.DisplayName, normalized, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(item.DisplayName, StringComparison.OrdinalIgnoreCase)
            || item.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
