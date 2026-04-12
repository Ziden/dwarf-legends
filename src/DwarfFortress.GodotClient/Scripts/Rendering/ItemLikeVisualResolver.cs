using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct ItemLikeVisualDescriptor(
    WorldSpriteVisual Visual,
    bool ShowCarriedIndicator,
    int StoredItemCount,
    int Capacity,
    WorldSpriteVisual[] PreviewItems)
{
    public bool HasStorageDecorator => Capacity > 0;
}

public static class ItemLikeVisualResolver
{
    private static readonly Vector2 ContainerEntityPixelSize = new(40f, 40f);

    public static ItemLikeVisualDescriptor ResolveLooseItem(Item item, ItemSystem? items, bool includeStoragePreview = true)
    {
        return BuildDescriptor(
            WorldSpriteVisuals.Item(item.DefId, item.MaterialId),
            item.CarriedByEntityId >= 0,
            item.Components.TryGet<ContainerComponent>(),
            items,
            includeStoragePreview);
    }

    public static bool TryResolveContainerEntity(Entity entity, ItemSystem? items, out ItemLikeVisualDescriptor descriptor, bool includeStoragePreview = true)
    {
        descriptor = default;
        if (entity is Item)
            return false;

        var container = entity.Components.TryGet<ContainerComponent>();
        if (container is null)
            return false;

        var baseVisual = WorldSpriteVisuals.Item(entity.DefId);
        descriptor = BuildDescriptor(
            new WorldSpriteVisual(baseVisual.Texture, ContainerEntityPixelSize),
            showCarriedIndicator: false,
            container,
            items,
            includeStoragePreview);
        return true;
    }

    private static ItemLikeVisualDescriptor BuildDescriptor(
        WorldSpriteVisual visual,
        bool showCarriedIndicator,
        ContainerComponent? container,
        ItemSystem? items,
        bool includeStoragePreview)
    {
        return new ItemLikeVisualDescriptor(
            visual,
            showCarriedIndicator,
            container?.Count ?? 0,
            container?.Capacity ?? 0,
            includeStoragePreview ? ResolveStoragePreviewItems(container, items) : []);
    }

    private static WorldSpriteVisual[] ResolveStoragePreviewItems(ContainerComponent? container, ItemSystem? items)
    {
        if (items is null || container is null || container.IsEmpty)
            return [];

        var groupedItems = new Dictionary<ItemVisualKey, (Item Representative, int Count)>();
        foreach (var itemId in container.StoredItemIds)
        {
            if (!items.TryGetItem(itemId, out var item) || item is null)
                continue;

            var key = new ItemVisualKey(item.DefId, item.MaterialId);
            if (groupedItems.TryGetValue(key, out var existing))
                groupedItems[key] = (existing.Representative, existing.Count + 1);
            else
                groupedItems[key] = (item, 1);
        }

        return groupedItems
            .OrderByDescending(entry => entry.Value.Count)
            .ThenBy(entry => entry.Key.DefId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.MaterialId ?? string.Empty, StringComparer.Ordinal)
            .Take(3)
            .Select(entry => WorldSpriteVisuals.Item(entry.Value.Representative.DefId, entry.Value.Representative.MaterialId))
            .ToArray();
    }

    private readonly record struct ItemVisualKey(string DefId, string? MaterialId);
}
