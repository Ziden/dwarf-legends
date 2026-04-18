using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GodotClient.Rendering;

public sealed class WorldChunkRenderSnapshot
{
    private readonly TileData[] _tiles;

    private WorldChunkRenderSnapshot(Vec3i origin, TileData[] tiles, int version, bool isPreview)
    {
        Origin = origin;
        _tiles = tiles;
        Version = version;
        IsPreview = isPreview;
    }

    public Vec3i Origin { get; }

    public int Version { get; }

    public bool IsPreview { get; }

    public static WorldChunkRenderSnapshot Capture(Chunk chunk)
    {
        var tiles = new TileData[Chunk.Width * Chunk.Height * Chunk.Depth];
        var index = 0;

        for (var localZ = 0; localZ < Chunk.Depth; localZ++)
        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
            tiles[index++] = chunk.Get(localX, localY, localZ);

        return new WorldChunkRenderSnapshot(chunk.Origin, tiles, chunk.Version, isPreview: false);
    }

    public static WorldChunkRenderSnapshot Capture(ChunkTileSnapshot snapshot)
    {
        var tiles = new TileData[Chunk.Width * Chunk.Height * Chunk.Depth];
        var index = 0;

        for (var localZ = 0; localZ < Chunk.Depth; localZ++)
        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            snapshot.TryGetLocalTile(localX, localY, localZ, out var tile);
            tiles[index++] = tile;
        }

        return new WorldChunkRenderSnapshot(snapshot.Origin, tiles, snapshot.Version, snapshot.IsPreview);
    }

    public bool ContainsWorldZ(int worldZ)
        => worldZ >= Origin.Z && worldZ < Origin.Z + Chunk.Depth;

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