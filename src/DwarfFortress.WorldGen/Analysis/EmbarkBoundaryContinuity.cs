using System;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Analysis;

[Flags]
public enum EmbarkBoundaryMismatchKind
{
    None = 0,
    SurfaceFamily = 1 << 0,
    Water = 1 << 1,
    Ecology = 1 << 2,
    Tree = 1 << 3,
}

public readonly record struct EmbarkBoundaryComparison(
    int SampleCount,
    int SurfaceFamilyMismatchCount,
    int WaterMismatchCount,
    int EcologyMismatchCount,
    int TreeMismatchCount)
{
    public float SurfaceFamilyMismatchRatio => Ratio(SurfaceFamilyMismatchCount, SampleCount);
    public float WaterMismatchRatio => Ratio(WaterMismatchCount, SampleCount);
    public float EcologyMismatchRatio => Ratio(EcologyMismatchCount, SampleCount);
    public float TreeMismatchRatio => Ratio(TreeMismatchCount, SampleCount);

    private static float Ratio(int numerator, int denominator)
        => denominator <= 0 ? 0f : numerator / (float)denominator;
}

public static class EmbarkBoundaryContinuity
{
    public static EmbarkBoundaryComparison CompareBoundary(GeneratedEmbarkMap map, GeneratedEmbarkMap neighbor, bool isEastNeighbor, int z = 0)
        => CompareBoundaryBand(map, neighbor, isEastNeighbor, bandWidth: 1, z);

    public static EmbarkBoundaryComparison CompareBoundaryBand(
        GeneratedEmbarkMap map,
        GeneratedEmbarkMap neighbor,
        bool isEastNeighbor,
        int bandWidth,
        int z = 0)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(neighbor);
        if (bandWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandWidth));

        if (z < 0 || z >= map.Depth || z >= neighbor.Depth)
            throw new ArgumentOutOfRangeException(nameof(z));

        var sampleLength = isEastNeighbor
            ? Math.Min(map.Height, neighbor.Height)
            : Math.Min(map.Width, neighbor.Width);
        var actualBandWidth = isEastNeighbor
            ? Math.Min(Math.Min(map.Width, neighbor.Width), bandWidth)
            : Math.Min(Math.Min(map.Height, neighbor.Height), bandWidth);
        var sampleCount = sampleLength * actualBandWidth;
        var surfaceFamilyMismatchCount = 0;
        var waterMismatchCount = 0;
        var ecologyMismatchCount = 0;
        var treeMismatchCount = 0;

        for (var index = 0; index < sampleLength; index++)
        {
            for (var offset = 0; offset < actualBandWidth; offset++)
            {
                var localTile = isEastNeighbor
                    ? map.GetTile(map.Width - 1 - offset, index, z)
                    : map.GetTile(index, map.Height - 1 - offset, z);
                var neighborTile = isEastNeighbor
                    ? neighbor.GetTile(offset, index, z)
                    : neighbor.GetTile(index, offset, z);

                var mismatch = CompareTiles(localTile, neighborTile);
                if ((mismatch & EmbarkBoundaryMismatchKind.SurfaceFamily) != 0)
                    surfaceFamilyMismatchCount++;
                if ((mismatch & EmbarkBoundaryMismatchKind.Water) != 0)
                    waterMismatchCount++;
                if ((mismatch & EmbarkBoundaryMismatchKind.Ecology) != 0)
                    ecologyMismatchCount++;
                if ((mismatch & EmbarkBoundaryMismatchKind.Tree) != 0)
                    treeMismatchCount++;
            }
        }

        return new EmbarkBoundaryComparison(
            SampleCount: sampleCount,
            SurfaceFamilyMismatchCount: surfaceFamilyMismatchCount,
            WaterMismatchCount: waterMismatchCount,
            EcologyMismatchCount: ecologyMismatchCount,
            TreeMismatchCount: treeMismatchCount);
    }

    public static EmbarkBoundaryMismatchKind CompareTiles(GeneratedTile tile, GeneratedTile neighbor)
    {
        var mismatch = EmbarkBoundaryMismatchKind.None;

        if (!string.Equals(ResolveSurfaceFamily(tile), ResolveSurfaceFamily(neighbor), StringComparison.Ordinal))
            mismatch |= EmbarkBoundaryMismatchKind.SurfaceFamily;

        var localHasWater = HasSurfaceWater(tile);
        var neighborHasWater = HasSurfaceWater(neighbor);
        if (localHasWater != neighborHasWater ||
            (localHasWater && Math.Abs(tile.FluidLevel - neighbor.FluidLevel) > 1))
        {
            mismatch |= EmbarkBoundaryMismatchKind.Water;
        }

        var localHasTree = IsTree(tile);
        var neighborHasTree = IsTree(neighbor);
        if (localHasTree != neighborHasTree ||
            (localHasTree && neighborHasTree &&
             !string.Equals(tile.TreeSpeciesId, neighbor.TreeSpeciesId, StringComparison.OrdinalIgnoreCase)))
        {
            mismatch |= EmbarkBoundaryMismatchKind.Tree;
        }

        var localHasPlant = HasPlant(tile);
        var neighborHasPlant = HasPlant(neighbor);
        if (localHasPlant != neighborHasPlant ||
            (localHasPlant && neighborHasPlant &&
             !string.Equals(tile.PlantDefId, neighbor.PlantDefId, StringComparison.OrdinalIgnoreCase)))
        {
            mismatch |= EmbarkBoundaryMismatchKind.Ecology;
        }

        return mismatch;
    }

    public static string ResolveSurfaceFamily(GeneratedTile tile)
    {
        if (HasSurfaceWater(tile))
            return "water";
        if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
            return "magma";
        if (tile.TileDefId == GeneratedTileDefIds.StoneBrick)
            return "road";
        if (tile.TileDefId == GeneratedTileDefIds.Tree)
            return "tree";
        if (tile.TileDefId == GeneratedTileDefIds.Sand)
            return "sand";
        if (tile.TileDefId == GeneratedTileDefIds.Mud)
            return "mud";
        if (tile.TileDefId == GeneratedTileDefIds.Snow)
            return "snow";
        if (tile.TileDefId == GeneratedTileDefIds.SoilWall ||
            tile.TileDefId == GeneratedTileDefIds.Soil ||
            tile.TileDefId == GeneratedTileDefIds.Grass)
        {
            return "soil";
        }

        if (tile.TileDefId == GeneratedTileDefIds.StoneFloor || tile.TileDefId == GeneratedTileDefIds.StoneWall)
            return "stone";
        if (tile.TileDefId == GeneratedTileDefIds.Empty)
            return "empty";

        return tile.TileDefId;
    }

    public static bool IsBoundaryCell(GeneratedEmbarkMap map, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(map);
        return x == 0 || y == 0 || x == map.Width - 1 || y == map.Height - 1;
    }

    public static int CountUnsafeBorderCells(GeneratedEmbarkMap map, int z = 0)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (z < 0 || z >= map.Depth)
            throw new ArgumentOutOfRangeException(nameof(z));

        var count = 0;
        for (var x = 0; x < map.Width; x++)
        {
            if (!IsSafePassableSurface(map.GetTile(x, 0, z)))
                count++;
            if (map.Height > 1 && !IsSafePassableSurface(map.GetTile(x, map.Height - 1, z)))
                count++;
        }

        for (var y = 1; y < map.Height - 1; y++)
        {
            if (!IsSafePassableSurface(map.GetTile(0, y, z)))
                count++;
            if (map.Width > 1 && !IsSafePassableSurface(map.GetTile(map.Width - 1, y, z)))
                count++;
        }

        return count;
    }

    public static bool IsSafePassableSurface(GeneratedTile tile)
        => tile.IsPassable &&
           tile.FluidType != GeneratedFluidType.Magma &&
           tile.TileDefId != GeneratedTileDefIds.Magma;

    private static bool HasSurfaceWater(GeneratedTile tile)
        => tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water;

    private static bool IsTree(GeneratedTile tile)
        => tile.TileDefId == GeneratedTileDefIds.Tree;

    private static bool HasPlant(GeneratedTile tile)
        => !string.IsNullOrWhiteSpace(tile.PlantDefId);
}