using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Harvests a mature wild plant or tree-borne fruit canopy and hands the yield to the harvester.
/// </summary>
public sealed class HarvestPlantStrategy : IJobStrategy
{
    public string JobDefId => JobDefIds.HarvestPlant;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var data = ctx.TryGet<DataManager>();
        return data is not null &&
               PlantHarvesting.TryGetHarvestablePlant(map, data, job.TargetPos, out _) &&
               PlantHarvesting.ResolveHarvestStandPosition(map, job.TargetPos).HasValue;
    }

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        var map = ctx.Get<WorldMap>();
        var standPos = PlantHarvesting.ResolveHarvestStandPosition(map, job.TargetPos)
            ?? job.TargetPos;

        return
        [
            new MoveToStep(standPos),
            new WorkAtStep(Duration: 2.5f, AnimationHint: "gather_plants", RequiredPosition: standPos),
        ];
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx) { }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        if (!PlantHarvesting.TryHarvestPlant(ctx, job.TargetPos, dropHarvestItem: true, dropSeedItem: true, out var result))
            return;

        var itemSystem = ctx.TryGet<ItemSystem>();
        var registry = ctx.Get<EntityRegistry>();
        if (itemSystem is not null && registry.TryGetById<Dwarf>(dwarfId, out var dwarf) && dwarf is not null)
        {
            var carrierPos = dwarf.Position.Position;
            if (result.HarvestItemEntityId.HasValue)
                itemSystem.PickUpItem(result.HarvestItemEntityId.Value, dwarfId, carrierPos);
            if (result.SeedItemEntityId.HasValue)
                itemSystem.PickUpItem(result.SeedItemEntityId.Value, dwarfId, carrierPos);
        }

        ctx.EventBus.Emit(new EntityActivityEvent(dwarfId, $"Harvested {result.HarvestDisplayName}", job.TargetPos));
    }
}