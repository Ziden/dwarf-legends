using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Jobs.Strategies;

internal static class ItemPickupHelper
{
    public static MoveToStep CreatePickupMoveStep(Item item)
        => RequiresStandOff(item)
            ? new MoveToStep(item.Position.Position, AcceptableDistance: 1, PreferAdjacent: true)
            : new MoveToStep(item.Position.Position);

    public static Vec3i? ResolveConsumeWorkPosition(Item item)
        => RequiresStandOff(item) ? null : item.Position.Position;

    private static bool RequiresStandOff(Item item)
        => item.ContainerItemId >= 0 || item.StockpileId >= 0;
}