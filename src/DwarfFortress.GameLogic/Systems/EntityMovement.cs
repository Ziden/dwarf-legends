using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

/// <summary>
/// Canonical helper for applying entity position changes.
/// Callers remain responsible for pathfinding and traversal validation; this helper
/// keeps the mutation side effects consistent across systems.
/// </summary>
public static class EntityMovement
{
    public static bool TryMove(GameContext ctx, Entity entity, Vec3i newPos, float? exactDurationSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(entity);

        if (!entity.Components.Has<PositionComponent>())
            return false;

        var posComp = entity.Components.Get<PositionComponent>();
        var oldPos = posComp.Position;
        if (oldPos == newPos)
            return false;

        posComp.Position = newPos;
        ctx.TryGet<ItemSystem>()?.UpdateCarriedItemsPosition(entity.Id, newPos);

        if (exactDurationSeconds.HasValue)
            ctx.TryGet<MovementPresentationSystem>()?.RecordEntityMovement(entity.Id, oldPos, newPos, exactDurationSeconds.Value);

        ctx.EventBus.Emit(new EntityMovedEvent(entity.Id, oldPos, newPos));
        return true;
    }
}