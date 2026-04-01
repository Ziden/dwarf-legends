using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Cuts a tree tile and spawns a log item.
/// </summary>
public sealed class CutTreeStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.CutTree;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var tile = map.GetTile(job.TargetPos);
        return tile.TileDefId == TileDefIds.Tree && tile.IsDesignated && TerrainClearanceHelper.FindAdjacentPassable(map, job.TargetPos).HasValue;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var chopFromPos = TerrainClearanceHelper.FindAdjacentPassable(map, job.TargetPos)
            ?? job.TargetPos + Vec3i.South; // fallback (should pass CanExecute)

        return new ActionStep[]
        {
            new MoveToStep(chopFromPos),
            new WorkAtStep(Duration: 4f, AnimationHint: "wood_cutting", RequiredPosition: chopFromPos),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var map  = ctx.Get<WorldMap>();
        var tile = map.GetTile(job.TargetPos);
        var logMaterialId = ResolveLogMaterialId(ctx, tile.TreeSpeciesId);
        var clearedTerrain = TerrainClearanceHelper.ResolveClearedTerrain(ctx, map, job.TargetPos, tile.MaterialId);
        tile.TileDefId  = clearedTerrain.TileDefId;
        tile.MaterialId = clearedTerrain.MaterialId;
        tile.TreeSpeciesId = null;
        tile.IsDesignated = false;
        tile.IsPassable = true;
        map.SetTile(job.TargetPos, tile);

        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        itemSystem?.CreateItem(Items.ItemDefIds.Log, logMaterialId, job.TargetPos);
    }

    private static string ResolveLogMaterialId(GameContext ctx, string? treeSpeciesId)
    {
        const string defaultMaterialId = MaterialIds.Wood;
        if (string.IsNullOrWhiteSpace(treeSpeciesId))
            return defaultMaterialId;

        var normalizedSpeciesId = treeSpeciesId.Trim().ToLowerInvariant();
        var data = ctx.TryGet<DataManager>();
        if (data is null)
            return defaultMaterialId;

        var speciesWoodMaterialId = $"{normalizedSpeciesId}_wood";
        if (data.Materials.Contains(speciesWoodMaterialId))
            return speciesWoodMaterialId;

        if (data.Materials.Contains(normalizedSpeciesId))
            return normalizedSpeciesId;

        return defaultMaterialId;
    }

}
