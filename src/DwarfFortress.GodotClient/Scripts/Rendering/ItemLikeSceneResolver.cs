using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct ItemLikeSceneEntry(
    int RuntimeId,
    Vec3i TilePosition,
    string NodeName,
    ItemLikeVisualDescriptor Descriptor,
    MovementPresentationSegment? MovementSegment);

public static class ItemLikeSceneResolver
{
    public static void CollectVisibleEntries(
        EntityRegistry registry,
        ItemSystem items,
        SpatialIndexSystem spatial,
        MovementPresentationSystem? movementPresentation,
        int currentZ,
        int minX,
        int minY,
        int maxX,
        int maxY,
        List<int> scratchIds,
        List<ItemLikeSceneEntry> results,
        int maxCount)
    {
        results.Clear();
        scratchIds.Clear();
        if (maxCount <= 0)
            return;

        items.CollectLooseItemsInBounds(currentZ, minX, minY, maxX, maxY, scratchIds);
        spatial.CollectContainersInBounds(currentZ, minX, minY, maxX, maxY, scratchIds);
        if (scratchIds.Count > maxCount)
            scratchIds.RemoveRange(maxCount, scratchIds.Count - maxCount);

        foreach (var itemLikeId in scratchIds)
        {
            if (TryResolveVisibleEntry(registry, items, movementPresentation, itemLikeId, out var entry))
                results.Add(entry);
        }
    }

    private static bool TryResolveVisibleEntry(
        EntityRegistry registry,
        ItemSystem items,
        MovementPresentationSystem? movementPresentation,
        int itemLikeId,
        out ItemLikeSceneEntry entry)
    {
        if (items.TryGetItem(itemLikeId, out var item) && item is not null && ItemSystem.IsLooseWorldItem(item))
        {
            var itemSegment = movementPresentation?.TryGetItemSegment(item.Id, out var itemMovementSegment) == true
                ? itemMovementSegment
                : (MovementPresentationSegment?)null;
            entry = new ItemLikeSceneEntry(
                item.Id,
                item.Position.Position,
                $"Item_{item.Id}",
                ItemLikeVisualResolver.ResolveLooseItem(item, items, includeStoragePreview: false),
                itemSegment);
            return true;
        }

        if (registry.TryGetById(itemLikeId) is not Entity entity ||
            !ItemLikeVisualResolver.TryResolveContainerEntity(entity, items, out var containerDescriptor, includeStoragePreview: false))
        {
            entry = default;
            return false;
        }

        var position = entity.Components.TryGet<PositionComponent>();
        if (position is null)
        {
            entry = default;
            return false;
        }

        var entitySegment = movementPresentation?.TryGetEntitySegment(entity.Id, out var entityMovementSegment) == true
            ? entityMovementSegment
            : (MovementPresentationSegment?)null;
        entry = new ItemLikeSceneEntry(
            entity.Id,
            position.Position,
            $"Container_{entity.Id}",
            containerDescriptor,
            entitySegment);
        return true;
    }
}
