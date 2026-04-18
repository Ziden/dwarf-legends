using System;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

public sealed class ChunkTileSnapshot
{
    private readonly TileData[] _tiles;

    private ChunkTileSnapshot(Vec3i origin, TileData[] tiles, int version, bool isPreview)
    {
        Origin = origin;
        _tiles = tiles;
        Version = version;
        IsPreview = isPreview;
    }

    public Vec3i Origin { get; }

    public int Version { get; }

    public bool IsPreview { get; }

    public static ChunkTileSnapshot Create(Vec3i origin, TileData[] tiles, int version, bool isPreview = false)
    {
        ValidateTiles(tiles);

        return new ChunkTileSnapshot(origin, (TileData[])tiles.Clone(), version, isPreview);
    }

    internal static ChunkTileSnapshot CreateOwned(Vec3i origin, TileData[] tiles, int version, bool isPreview = false)
    {
        ValidateTiles(tiles);
        return new ChunkTileSnapshot(origin, tiles, version, isPreview);
    }

    private static void ValidateTiles(TileData[] tiles)
    {
        if (tiles is null)
            throw new ArgumentNullException(nameof(tiles));

        if (tiles.Length != Chunk.Width * Chunk.Height * Chunk.Depth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tiles),
                $"Chunk tile snapshot requires exactly {Chunk.Width * Chunk.Height * Chunk.Depth} tiles.");
        }
    }

    public bool TryGetLocalTile(int localX, int localY, int localZ, out TileData tile)
    {
        if (!Chunk.IsLocalInBounds(localX, localY, localZ))
        {
            tile = TileData.Empty;
            return false;
        }

        tile = _tiles[Index(localX, localY, localZ)];
        return true;
    }

    private static int Index(int localX, int localY, int localZ)
        => (localZ * Chunk.Width * Chunk.Height) + (localY * Chunk.Width) + localX;
}