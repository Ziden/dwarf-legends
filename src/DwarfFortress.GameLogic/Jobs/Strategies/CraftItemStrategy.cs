using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

/// <summary>
/// Handles craft_item jobs created by RecipeSystem.
/// <c>job.TargetPos</c> is the workshop's world origin; <c>job.EntityId</c> is the workshop entity ID.
/// </summary>
public sealed class CraftItemStrategy : IJobStrategy
{
    private const float AdHocCraftWorkTime = 0.1f;

    public string JobDefId => JobDefIds.Craft;

    public bool CanExecute(Job job, int dwarfId, GameContext ctx)
        => job.EntityId < 0 || TryGetRecipe(job, ctx) is not null;

    public IReadOnlyList<ActionStep> GetSteps(Job job, int dwarfId, GameContext ctx)
    {
        if (job.EntityId < 0)
        {
            return
            [
                new MoveToStep(job.TargetPos),
                new WorkAtStep(AdHocCraftWorkTime, AnimationHint: "crafting", RequiredPosition: job.TargetPos),
            ];
        }

        var itemSystem = ctx.TryGet<ItemSystem>();
        var recipe = TryGetRecipe(job, ctx);
        if (itemSystem is null || recipe is null)
            return System.Array.Empty<ActionStep>();

        if (!itemSystem.TryReserveRecipeInputs(recipe, job.ReservedItemIds))
            return System.Array.Empty<ActionStep>();

        var steps = new List<ActionStep>();
        foreach (var itemId in job.ReservedItemIds)
        {
            if (!itemSystem.TryGetItem(itemId, out var item) || item is null)
                continue;

            var itemPos = item.Position.Position;
            if (itemPos != job.TargetPos)
            {
                steps.Add(new MoveToStep(itemPos));
                steps.Add(new PickUpItemStep(itemId));
                steps.Add(new MoveToStep(job.TargetPos));
                steps.Add(new PlaceItemStep(itemId, job.TargetPos, job.EntityId));
            }
        }

        steps.Add(new MoveToStep(job.TargetPos));
        steps.Add(new WorkAtStep(Duration: recipe.WorkTime, AnimationHint: "crafting", RequiredPosition: job.TargetPos));
        return steps;
    }

    public void OnInterrupt(Job job, int dwarfId, GameContext ctx)
    {
        var itemSystem = ctx.TryGet<ItemSystem>();
        if (itemSystem is null) return;
        var registry = ctx.Get<Entities.EntityRegistry>();
        var dropPos = registry.TryGetById<Entities.Dwarf>(dwarfId, out var dwarf) && dwarf is not null
            ? dwarf.Position.Position
            : job.TargetPos;

        foreach (var itemId in job.ReservedItemIds)
            if (itemSystem.TryGetItem(itemId, out var item) && item is not null)
            {
                if (item.CarriedByEntityId == dwarfId)
                    itemSystem.ReleaseCarriedItem(itemId, dropPos);
                item.IsClaimed = false;
            }

        job.ReservedItemIds.Clear();
    }

    public void OnComplete(Job job, int dwarfId, GameContext ctx)
    {
        // RecipeSystem listens to JobCompletedEvent and handles all
        // ingredient consumption and output item creation.
    }

    private static Data.Defs.RecipeDef? TryGetRecipe(Job job, GameContext ctx)
    {
        var recipeSystem = ctx.TryGet<RecipeSystem>();
        var dataManager = ctx.TryGet<DataManager>();
        if (recipeSystem is null || dataManager is null || job.EntityId < 0)
            return null;

        var order = recipeSystem.GetOrCreateQueue(job.EntityId).Peek();
        if (order is null || !dataManager.Recipes.Contains(order.RecipeId))
            return null;

        return dataManager.Recipes.Get(order.RecipeId);
    }
}
