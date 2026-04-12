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
    MovementPresentationSegment? MovementSegment,
    int CarrierEntityId = -1,
    ItemCarryMode CarryMode = ItemCarryMode.None);

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
        IReadOnlyList<int> visibleDwarfIds,
        IReadOnlyList<int> visibleCreatureIds,
        List<int> looseItemIds,
        List<int> containerIds,
        List<ItemLikeSceneEntry> results,
        int maxCount)
    {
        results.Clear();
        looseItemIds.Clear();
        containerIds.Clear();
        if (maxCount <= 0)
            return;

        foreach (var dwarfId in visibleDwarfIds)
        {
            if (results.Count >= maxCount)
                break;

            if (TryResolveHauledEntry(items, movementPresentation, dwarfId, out var hauledEntry))
                results.Add(hauledEntry);
        }

        foreach (var creatureId in visibleCreatureIds)
        {
            if (results.Count >= maxCount)
                break;

            if (TryResolveHauledEntry(items, movementPresentation, creatureId, out var hauledEntry))
                results.Add(hauledEntry);
        }

        items.CollectLooseItemsInBounds(currentZ, minX, minY, maxX, maxY, looseItemIds);
        spatial.CollectContainersInBounds(currentZ, minX, minY, maxX, maxY, containerIds);

        foreach (var itemId in looseItemIds)
        {
            if (results.Count >= maxCount)
                break;

            if (TryResolveVisibleEntry(registry, items, movementPresentation, itemId, out var entry))
                results.Add(entry);
        }

        foreach (var containerId in containerIds)
        {
            if (results.Count >= maxCount)
                break;

            if (TryResolveVisibleEntry(registry, items, movementPresentation, containerId, out var entry))
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
                itemSegment,
                item.CarriedByEntityId,
                item.CarryMode);
            return true;
        }

        if (registry.TryGetById(itemLikeId) is not Entity entity ||
            !ItemLikeVisualResolver.TryResolveContainerEntity(entity, items, out var containerDescriptor, includeStoragePreview: true))
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

    private static bool TryResolveHauledEntry(
        ItemSystem items,
        MovementPresentationSystem? movementPresentation,
        int carrierEntityId,
        out ItemLikeSceneEntry entry)
    {
        entry = default;
        if (!items.TryGetHauledItem(carrierEntityId, out var item) ||
            item is null ||
            item.CarryMode != ItemCarryMode.Hauling)
        {
            return false;
        }

        var itemSegment = movementPresentation?.TryGetItemSegment(item.Id, out var itemMovementSegment) == true
            ? itemMovementSegment
            : (MovementPresentationSegment?)null;
        entry = new ItemLikeSceneEntry(
            item.Id,
            item.Position.Position,
            $"Item_{item.Id}",
            ItemLikeVisualResolver.ResolveLooseItem(item, items, includeStoragePreview: false),
            itemSegment,
            carrierEntityId,
            item.CarryMode);
        return true;
    }
}
