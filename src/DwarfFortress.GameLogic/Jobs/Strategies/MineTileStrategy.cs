using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Executes a mining designation: move to an adjacent passable tile, dig, spawn a boulder/ore.
/// </summary>
public sealed class MineTileStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.MineTile;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var tile = map.GetTile(job.TargetPos);
        return tile.IsDesignated && TerrainClearanceHelper.FindAdjacentPassable(map, job.TargetPos).HasValue;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var digFromPos = TerrainClearanceHelper.FindAdjacentPassable(map, job.TargetPos)
                         ?? job.TargetPos + Vec3i.South; // fallback (should pass CanExecute)
        return new ActionStep[]
        {
            new MoveToStep(digFromPos),
            new WorkAtStep(Duration: 5f, AnimationHint: "mining", RequiredPosition: digFromPos),
        };
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var tile = map.GetTile(job.TargetPos);
        var originalMaterialId = tile.MaterialId;

        // Capture default wall drop and embedded ore before changing tile identity.
        var dm = ctx.TryGet<DataManager>();
        var def = dm?.Tiles.GetOrNull(tile.TileDefId);
        var dropId = def?.DropItemDefId ?? dm?.ContentQueries?.ResolveMineableBoulderForm(originalMaterialId);
        var oreDropId = tile.OreItemDefId;
        var isAquifer = tile.IsAquifer;
        var clearedTerrain = TerrainClearanceHelper.ResolveClearedTerrain(ctx, map, job.TargetPos, tile.MaterialId);

        tile.TileDefId = clearedTerrain.TileDefId;
        tile.MaterialId = clearedTerrain.MaterialId;
        tile.TreeSpeciesId = null;
        tile.OreItemDefId = null;
        tile.IsAquifer = false;
        tile.IsDesignated = false;
        tile.IsPassable = true;
        if (isAquifer)
        {
            tile.FluidType = FluidType.Water;
            tile.FluidLevel = tile.FluidLevel >= 3 ? tile.FluidLevel : (byte)3;
        }

        map.SetTile(job.TargetPos, tile);

        var resolvedDropId = oreDropId ?? dropId;
        var itemSystem = ctx.TryGet<Systems.ItemSystem>();
        if (itemSystem == null || resolvedDropId == null)
            return;

        itemSystem.CreateItem(resolvedDropId, originalMaterialId ?? tile.MaterialId ?? "stone", job.TargetPos);
    }
}
