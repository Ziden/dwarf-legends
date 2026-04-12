using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

internal static class DrinkItemLocator
{
    public static Item? FindReachableDrinkItem(GameContext ctx, WorldMap map, Vec3i origin, Item? preferredItem = null)
    {
        if (preferredItem is not null && ItemPickupHelper.CanReachForPickup(map, origin, preferredItem))
            return preferredItem;

        var itemSystem = ctx.TryGet<ItemSystem>();
        var data = ctx.TryGet<DataManager>();
        if (itemSystem is null || data is null)
            return null;

        foreach (var candidate in EnumerateDrinkCandidates(ctx, itemSystem, data))
        {
            if (preferredItem is not null && candidate.Id == preferredItem.Id)
                continue;

            if (ItemPickupHelper.CanReachForPickup(map, origin, candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<Item> EnumerateDrinkCandidates(GameContext ctx, ItemSystem itemSystem, DataManager data)
    {
        foreach (var item in itemSystem.GetUsableItems())
            if (IsDrinkItem(item, data))
                yield return item;

        var registry = ctx.TryGet<EntityRegistry>();
        if (registry is null)
            yield break;

        foreach (var box in registry.GetAlive<Box>())
        {
            foreach (var itemId in box.Container.StoredItemIds)
            {
                if (!itemSystem.TryGetItem(itemId, out var item) || item is null || item.IsClaimed)
                    continue;

                if (IsDrinkItem(item, data))
                    yield return item;
            }
        }
    }

    private static bool IsDrinkItem(Item item, DataManager data)
        => data.Items.GetOrNull(item.DefId)?.Tags.Contains(TagIds.Drink) == true;
}