using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.WorldGen.Generation;

namespace DwarfFortress.GameLogic.World;

public readonly record struct TileBounds(int X, int Y, int Width, int Height)
{
    public static TileBounds Empty => new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int MinX => X;
    public int MinY => Y;
    public int MaxX => IsEmpty ? X - 1 : X + Width - 1;
    public int MaxY => IsEmpty ? Y - 1 : Y + Height - 1;
}

public readonly record struct ChunkCoverageBounds(int MinChunkX, int MinChunkY, int MaxChunkX, int MaxChunkY, int ChunkOriginZ)
{
    public static ChunkCoverageBounds Empty(int currentZ)
        => new(0, 0, -1, -1, StreamedChunkKey.AlignToChunkOriginForDepth(currentZ));

    public bool IsEmpty => MaxChunkX < MinChunkX || MaxChunkY < MinChunkY;
}

public readonly record struct ChunkViewportCoverage(
    RegionCoord RegionCoord,
    ChunkCoverageBounds VisibleChunkBounds,
    ChunkCoverageBounds ResidentChunkBounds);

public readonly record struct StreamedChunkKey(
    int WorldX,
    int WorldY,
    int RegionX,
    int RegionY,
    int ChunkOriginX,
    int ChunkOriginY,
    int ChunkOriginZ)
{
    public WorldCoord WorldCoord => new(WorldX, WorldY);
    public RegionCoord RegionCoord => new(WorldX, WorldY, RegionX, RegionY);
    public Vec3i ChunkOrigin => new(ChunkOriginX, ChunkOriginY, ChunkOriginZ);

    public static StreamedChunkKey FromChunkOrigin(RegionCoord regionCoord, Vec3i chunkOrigin)
    {
        if (!IsChunkAligned(chunkOrigin.X, Chunk.Width)
            || !IsChunkAligned(chunkOrigin.Y, Chunk.Height)
            || !IsChunkAligned(chunkOrigin.Z, Chunk.Depth))
        {
            throw new ArgumentOutOfRangeException(nameof(chunkOrigin), "Chunk origin must align to chunk dimensions.");
        }

        return new StreamedChunkKey(
            regionCoord.WorldX,
            regionCoord.WorldY,
            regionCoord.RegionX,
            regionCoord.RegionY,
            chunkOrigin.X,
            chunkOrigin.Y,
            chunkOrigin.Z);
    }

    public static StreamedChunkKey FromLocalTile(RegionCoord regionCoord, Vec3i localTilePosition)
        => FromChunkOrigin(regionCoord, ChunkOriginForLocalTile(localTilePosition));

    public static Vec3i ChunkOriginForLocalTile(Vec3i localTilePosition)
        => new(
            AlignToChunkOrigin(localTilePosition.X, Chunk.Width),
            AlignToChunkOrigin(localTilePosition.Y, Chunk.Height),
            AlignToChunkOrigin(localTilePosition.Z, Chunk.Depth));

    public static int AlignToChunkOriginForWidth(int coordinate)
        => AlignToChunkOrigin(coordinate, Chunk.Width);

    public static int AlignToChunkOriginForHeight(int coordinate)
        => AlignToChunkOrigin(coordinate, Chunk.Height);

    public static int AlignToChunkOriginForDepth(int coordinate)
        => AlignToChunkOrigin(coordinate, Chunk.Depth);

    public static ChunkCoverageBounds ResolveIntersectingChunkBounds(
        TileBounds tileBounds,
        int currentZ,
        int chunkMargin = 0)
    {
        if (tileBounds.IsEmpty)
            return ChunkCoverageBounds.Empty(currentZ);

        var resolvedMargin = Math.Max(0, chunkMargin);
        var minChunkX = AlignToChunkOrigin(tileBounds.MinX, Chunk.Width) - (resolvedMargin * Chunk.Width);
        var minChunkY = AlignToChunkOrigin(tileBounds.MinY, Chunk.Height) - (resolvedMargin * Chunk.Height);
        var chunkZ = AlignToChunkOrigin(currentZ, Chunk.Depth);
        var maxChunkX = AlignToChunkOrigin(tileBounds.MaxX, Chunk.Width) + (resolvedMargin * Chunk.Width);
        var maxChunkY = AlignToChunkOrigin(tileBounds.MaxY, Chunk.Height) + (resolvedMargin * Chunk.Height);
        return new ChunkCoverageBounds(minChunkX, minChunkY, maxChunkX, maxChunkY, chunkZ);
    }

    public static IEnumerable<StreamedChunkKey> EnumerateIntersecting(
        RegionCoord regionCoord,
        TileBounds tileBounds,
        int currentZ,
        int chunkMargin = 0)
    {
        var chunkBounds = ResolveIntersectingChunkBounds(tileBounds, currentZ, chunkMargin);
        if (chunkBounds.IsEmpty)
            yield break;

        for (var chunkX = chunkBounds.MinChunkX; chunkX <= chunkBounds.MaxChunkX; chunkX += Chunk.Width)
        for (var chunkY = chunkBounds.MinChunkY; chunkY <= chunkBounds.MaxChunkY; chunkY += Chunk.Height)
        {
            yield return new StreamedChunkKey(
                regionCoord.WorldX,
                regionCoord.WorldY,
                regionCoord.RegionX,
                regionCoord.RegionY,
                chunkX,
                chunkY,
                chunkBounds.ChunkOriginZ);
        }
    }

    private static int AlignToChunkOrigin(int coordinate, int chunkSize)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));

        var remainder = coordinate % chunkSize;
        if (remainder == 0)
            return coordinate;

        return coordinate >= 0
            ? coordinate - remainder
            : coordinate - remainder - chunkSize;
    }

    private static bool IsChunkAligned(int coordinate, int chunkSize)
        => coordinate == AlignToChunkOrigin(coordinate, chunkSize);
}

public readonly record struct ChunkViewportState(
    RegionCoord RegionCoord,
    int CurrentZ,
    TileBounds VisibleTileBounds,
    int PrefetchRadiusChunks = 1)
{
    public IEnumerable<StreamedChunkKey> EnumerateVisibleChunkKeys()
        => StreamedChunkKey.EnumerateIntersecting(RegionCoord, VisibleTileBounds, CurrentZ);

    public IEnumerable<StreamedChunkKey> EnumerateResidentChunkKeys()
        => StreamedChunkKey.EnumerateIntersecting(RegionCoord, VisibleTileBounds, CurrentZ, PrefetchRadiusChunks);

    public ChunkCoverageBounds ResolveVisibleChunkBounds()
        => StreamedChunkKey.ResolveIntersectingChunkBounds(VisibleTileBounds, CurrentZ);

    public ChunkCoverageBounds ResolveResidentChunkBounds()
        => StreamedChunkKey.ResolveIntersectingChunkBounds(VisibleTileBounds, CurrentZ, PrefetchRadiusChunks);

    public ChunkViewportCoverage ResolveCoverage()
        => new(RegionCoord, ResolveVisibleChunkBounds(), ResolveResidentChunkBounds());

    public bool HasEquivalentChunkCoverage(ChunkViewportState other)
        => ResolveCoverage() == other.ResolveCoverage();
}

public readonly record struct ChunkViewportChangedEvent(ChunkViewportState Viewport);