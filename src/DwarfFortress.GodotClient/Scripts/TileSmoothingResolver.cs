using System;

public static class TileSmoothingResolver
{
    public static TileTransitionSet<TValue> ResolveTransitions<TTile, TValue>(
        TValue baseValue,
        int x,
        int y,
        int z,
        Func<int, int, int, TTile?> tryGetTile,
        TileTransitionRuleProfile<TTile, TValue> profile)
        where TTile : struct
        where TValue : struct
    {
        if (!profile.CanSmoothBase(baseValue))
            return default;

        var cardinalNeighbors = ResolveCardinalNeighbors<TTile, TValue>(
            x,
            y,
            z,
            tryGetTile,
            tile =>
            {
                var neighbor = profile.ResolveNeighborValue(tile);
                if (neighbor is not { } resolvedNeighbor)
                    return null;

                return profile.ShouldBlendNeighbor(baseValue, resolvedNeighbor)
                    ? resolvedNeighbor
                    : null;
            });

        var cornerNeighbors = ResolveCorners(
            cardinalNeighbors,
            (first, second, corner) => profile.ResolveCorner(first, second, x, y, z, corner));

        return new TileTransitionSet<TValue>(
            cardinalNeighbors.North,
            cardinalNeighbors.South,
            cardinalNeighbors.West,
            cardinalNeighbors.East,
            cornerNeighbors.NorthWest,
            cornerNeighbors.NorthEast,
            cornerNeighbors.SouthWest,
            cornerNeighbors.SouthEast);
    }

    public static CardinalNeighborMatchSet ResolveMatches<TTile>(
        int x,
        int y,
        int z,
        Func<int, int, int, TTile?> tryGetTile,
        TileMatchRuleProfile<TTile> profile)
        where TTile : struct
    {
        return ResolveCardinalMatches(x, y, z, tryGetTile, profile.IsMatch);
    }

    public static CardinalNeighborSet<TNeighbor> ResolveCardinalNeighbors<TTile, TNeighbor>(
        int x,
        int y,
        int z,
        Func<int, int, int, TTile?> tryGetTile,
        Func<TTile?, TNeighbor?> resolveNeighbor)
        where TTile : struct
        where TNeighbor : struct
    {
        return new CardinalNeighborSet<TNeighbor>(
            North: resolveNeighbor(tryGetTile(x, y - 1, z)),
            South: resolveNeighbor(tryGetTile(x, y + 1, z)),
            West: resolveNeighbor(tryGetTile(x - 1, y, z)),
            East: resolveNeighbor(tryGetTile(x + 1, y, z)));
    }

    public static CardinalNeighborMatchSet ResolveCardinalMatches<TTile>(
        int x,
        int y,
        int z,
        Func<int, int, int, TTile?> tryGetTile,
        Func<TTile?, bool> isMatch)
        where TTile : struct
    {
        return new CardinalNeighborMatchSet(
            NorthMatch: isMatch(tryGetTile(x, y - 1, z)),
            SouthMatch: isMatch(tryGetTile(x, y + 1, z)),
            WestMatch: isMatch(tryGetTile(x - 1, y, z)),
            EastMatch: isMatch(tryGetTile(x + 1, y, z)));
    }

    public static CornerNeighborSet<TNeighbor> ResolveCorners<TNeighbor>(
        in CardinalNeighborSet<TNeighbor> cardinalNeighbors,
        Func<TNeighbor, TNeighbor, TerrainCornerQuadrant, TNeighbor> resolveCorner)
        where TNeighbor : struct
    {
        TNeighbor? northWest = null;
        TNeighbor? northEast = null;
        TNeighbor? southWest = null;
        TNeighbor? southEast = null;

        if (cardinalNeighbors.North is { } north && cardinalNeighbors.West is { } west)
            northWest = resolveCorner(north, west, TerrainCornerQuadrant.NorthWest);
        if (cardinalNeighbors.North is { } north2 && cardinalNeighbors.East is { } east)
            northEast = resolveCorner(north2, east, TerrainCornerQuadrant.NorthEast);
        if (cardinalNeighbors.South is { } south && cardinalNeighbors.West is { } west2)
            southWest = resolveCorner(south, west2, TerrainCornerQuadrant.SouthWest);
        if (cardinalNeighbors.South is { } south2 && cardinalNeighbors.East is { } east2)
            southEast = resolveCorner(south2, east2, TerrainCornerQuadrant.SouthEast);

        return new CornerNeighborSet<TNeighbor>(northWest, northEast, southWest, southEast);
    }
}