using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Jobs.Strategies;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Systems;

public readonly record struct HarvestablePlantTarget(Vec3i PlantPos, Vec3i StandPos, PlantDef PlantDef);

public readonly record struct PlantHarvestResult(
    string PlantDefId,
    string PlantDisplayName,
    string HarvestItemDefId,
    string HarvestDisplayName,
    string? SeedItemDefId,
    int? HarvestItemEntityId,
    int? SeedItemEntityId,
    bool DroppedHarvestItem,
    bool DroppedSeedItem);

public static class PlantHarvesting
{
    public static bool TryFindNearestHarvestablePlant(WorldMap map, DataManager data, Vec3i origin, int searchRadius, out HarvestablePlantTarget target)
    {
        target = default;
        var bestPathLength = int.MaxValue;
        var bestDistance = int.MaxValue;
        var found = false;

        for (var dx = -searchRadius; dx <= searchRadius; dx++)
        for (var dy = -searchRadius; dy <= searchRadius; dy++)
        {
            if (Math.Abs(dx) + Math.Abs(dy) > searchRadius)
                continue;

            var pos = new Vec3i(origin.X + dx, origin.Y + dy, origin.Z);
            if (!map.IsInBounds(pos))
                continue;
            if (!TryGetHarvestablePlant(map, data, pos, out var plantDef))
                continue;

            var standPos = ResolveHarvestStandPosition(map, pos);
            if (!standPos.HasValue)
                continue;

            var path = Pathfinder.FindPath(map, origin, standPos.Value);
            if (path.Count == 0)
                continue;

            var pathLength = path.Count;
            var distance = origin.ManhattanDistanceTo(pos);
            if (pathLength > bestPathLength || (pathLength == bestPathLength && distance >= bestDistance))
                continue;

            bestPathLength = pathLength;
            bestDistance = distance;
            target = new HarvestablePlantTarget(pos, standPos.Value, plantDef);
            found = true;
        }

        return found;
    }

    public static bool TryGetHarvestablePlant(WorldMap map, DataManager data, Vec3i pos, out PlantDef plantDef)
    {
        if (!map.IsInBounds(pos))
        {
            plantDef = null!;
            return false;
        }

        return TryGetHarvestablePlant(map.GetTile(pos), data, out plantDef);
    }

    public static bool TryGetHarvestablePlant(TileData tile, DataManager data, out PlantDef plantDef)
    {
        plantDef = null!;
        if (!tile.HasPlant || string.IsNullOrWhiteSpace(tile.PlantDefId) || tile.PlantYieldLevel == 0)
            return false;

        var resolvedPlantDef = data.Plants.GetOrNull(tile.PlantDefId);
        if (resolvedPlantDef is null)
            return false;

        plantDef = resolvedPlantDef;

        if (tile.PlantGrowthStage < PlantGrowthStages.Mature)
            return false;

        return !string.IsNullOrWhiteSpace(ResolveHarvestItemDefId(data, plantDef));
    }

    public static Vec3i? ResolveHarvestStandPosition(WorldMap map, Vec3i plantPos)
    {
        if (map.IsWalkable(plantPos))
            return plantPos;

        return TerrainClearanceHelper.FindAdjacentPassable(map, plantPos);
    }

    public static bool TryHarvestPlant(GameContext ctx, Vec3i pos, bool dropHarvestItem, bool dropSeedItem, out PlantHarvestResult result)
    {
        var map = ctx.Get<WorldMap>();
        var data = ctx.Get<DataManager>();
        if (!TryGetHarvestablePlant(map, data, pos, out var plantDef))
        {
            result = default;
            return false;
        }

        var tile = map.GetTile(pos);
        var harvestItemDefId = ResolveHarvestItemDefId(data, plantDef);
        if (string.IsNullOrWhiteSpace(harvestItemDefId))
        {
            result = default;
            return false;
        }

        var itemSystem = ctx.TryGet<ItemSystem>();
        var harvestItemEntityId = default(int?);
        var droppedHarvestItem = dropHarvestItem && itemSystem is not null;
        if (droppedHarvestItem)
            harvestItemEntityId = itemSystem!.CreateItem(harvestItemDefId, MaterialIds.Food, pos).Id;

        var seedItemDefId = tile.PlantSeedLevel > 0 && !string.IsNullOrWhiteSpace(data.ContentQueries?.ResolveSeedItemDefId(plantDef.Id) ?? plantDef.SeedItemDefId)
            ? (data.ContentQueries?.ResolveSeedItemDefId(plantDef.Id) ?? plantDef.SeedItemDefId)!
            : null;
        var seedItemEntityId = default(int?);
        var droppedSeedItem = dropSeedItem && seedItemDefId is not null && itemSystem is not null;
        if (droppedSeedItem)
            seedItemEntityId = itemSystem!.CreateItem(seedItemDefId!, MaterialIds.Food, pos).Id;

        ResetAfterHarvest(ref tile, plantDef);
        map.SetTile(pos, tile);

        var harvestDisplayName = data.Items.GetOrNull(harvestItemDefId)?.DisplayName ?? plantDef.DisplayName;
        result = new PlantHarvestResult(
            plantDef.Id,
            plantDef.DisplayName,
            harvestItemDefId,
            harvestDisplayName,
            seedItemDefId,
            harvestItemEntityId,
            seedItemEntityId,
            droppedHarvestItem,
            droppedSeedItem);
        return true;
    }

    private static void ResetAfterHarvest(ref TileData tile, PlantDef plantDef)
    {
        tile.IsDesignated = false;
        tile.PlantYieldLevel = 0;
        tile.PlantSeedLevel = 0;
        tile.PlantGrowthProgressSeconds = 0f;

        if (plantDef.HostKind == PlantHostKind.Tree)
        {
            tile.PlantGrowthStage = plantDef.MaxGrowthStage;
            return;
        }

        tile.PlantGrowthStage = (byte)Math.Max(PlantGrowthStages.Sprout, plantDef.MaxGrowthStage - 1);
    }

    private static string? ResolveHarvestItemDefId(DataManager data, PlantDef plantDef)
        => data.ContentQueries?.ResolveHarvestItemDefId(plantDef.Id)
           ?? (!string.IsNullOrWhiteSpace(plantDef.HarvestItemDefId)
               ? plantDef.HarvestItemDefId
               : plantDef.FruitItemDefId);
}
