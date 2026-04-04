using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
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
        if (tile.TileDefId != TileDefIds.Tree || !tile.IsDesignated)
            return false;

        var registry = ctx.TryGet<EntityRegistry>();
        if (registry is not null && registry.TryGetById<Dwarf>(dwarfId, out var dwarf) && dwarf is not null)
            return TerrainClearanceHelper.FindReachableAdjacentPassable(map, job.TargetPos, dwarf.Position.Position).HasValue;

        return TerrainClearanceHelper.FindAdjacentPassable(map, job.TargetPos).HasValue;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var chopFromPos = ResolveChopFromPos(ctx, map, job.TargetPos, dwarfId)
            ?? TerrainClearanceHelper.FindAdjacentPassable(map, job.TargetPos)
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
        var logItemDefId = ResolveLogItemDefId(ctx, logMaterialId);
        var clearedTerrain = TerrainClearanceHelper.ResolveClearedTerrain(ctx, map, job.TargetPos, tile.MaterialId);
        tile.TileDefId  = clearedTerrain.TileDefId;
        tile.MaterialId = clearedTerrain.MaterialId;
        tile.TreeSpeciesId = null;
        tile.IsDesignated = false;
        tile.IsPassable = true;
        map.SetTile(job.TargetPos, tile);

        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        var log = itemSystem?.CreateItem(logItemDefId, logMaterialId, job.TargetPos);
        if (log is not null)
        {
            ctx.TryGet<Systems.MovementPresentationSystem>()?.RecordItemMovement(
                log.Id,
                job.TargetPos + Vec3i.Up,
                job.TargetPos,
                0.45f,
                Systems.MovementPresentationMotionKind.Linear);
        }
    }

    private static Vec3i? ResolveChopFromPos(GameContext ctx, WorldMap map, Vec3i targetPos, int dwarfId)
    {
        var registry = ctx.TryGet<EntityRegistry>();
        if (registry is not null && registry.TryGetById<Dwarf>(dwarfId, out var dwarf) && dwarf is not null)
        {
            var reachable = TerrainClearanceHelper.FindReachableAdjacentPassable(map, targetPos, dwarf.Position.Position);
            if (reachable.HasValue)
                return reachable;
        }

        return TerrainClearanceHelper.FindAdjacentPassable(map, targetPos);
    }

    private static string ResolveLogMaterialId(GameContext ctx, string? treeSpeciesId)
    {
        const string defaultMaterialId = MaterialIds.Wood;
        if (string.IsNullOrWhiteSpace(treeSpeciesId))
            return defaultMaterialId;

        var data = ctx.TryGet<DataManager>();
        if (data is null)
            return defaultMaterialId;

        var resolvedMaterialId = data.ContentQueries?.ResolveTreeWoodMaterialId(treeSpeciesId);
        if (!string.IsNullOrWhiteSpace(resolvedMaterialId) && data.Materials.Contains(resolvedMaterialId))
            return resolvedMaterialId;

        return defaultMaterialId;
    }

    private static string ResolveLogItemDefId(GameContext ctx, string materialId)
    {
        var data = ctx.TryGet<DataManager>();
        var resolvedItemDefId = data?.ContentQueries?.ResolveLogItemDefId(materialId);
        return !string.IsNullOrWhiteSpace(resolvedItemDefId) && data!.Items.Contains(resolvedItemDefId)
            ? resolvedItemDefId
            : Items.ItemDefIds.Log;
    }

}
