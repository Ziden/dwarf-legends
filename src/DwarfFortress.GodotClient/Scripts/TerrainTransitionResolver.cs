using System;

public static class TerrainTransitionResolver
{
    public static TerrainTransitionSet Resolve(
        GroundVisualData baseGround,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        var transitions = TileSmoothingResolver.ResolveTransitions(
            baseGround,
            x,
            y,
            z,
            tryGetTile,
            GroundTileSmoothingProfile.Create(resolveGroundFromMaterial));
        return TerrainTransitionSet.FromTileTransitions(transitions);
    }
}
