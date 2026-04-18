using System;

namespace DwarfFortress.GodotClient.Rendering;

public static class TerrainTransitionResolver
{
    public static TerrainTransitionSet Resolve(
        GroundVisualData baseGround,
        int x,
        int y,
        int z,
        Func<int, int, int, GroundVisualData?> tryGetGround)
    {
        var transitions = TileSmoothingResolver.ResolveTransitions(
            baseGround,
            x,
            y,
            z,
            tryGetGround,
            GroundTileSmoothingProfile.Create());
        return TerrainTransitionSet.FromTileTransitions(transitions);
    }
}
