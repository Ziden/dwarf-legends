using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.World;

/// <summary>
/// A 16×16×4 block of tiles. The world is divided into chunks for efficient
/// dirty tracking and deferred rendering updates.
/// </summary>
public sealed class Chunk
{
    public const int Width  = 16;
    public const int Height = 16;
    public const int Depth  = 4;

    private readonly TileData[,,] _tiles = new TileData[Width, Height, Depth];

    /// <summary>World-space position of this chunk's (0,0,0) corner.</summary>
    public Vec3i Origin { get; }

    /// <summary>
    /// True when at least one tile has changed since the last render snapshot.
    /// Cleared by client-side consumers after they rebuild derived render state.
    /// </summary>
    public bool IsDirty { get; private set; } = true;

    public Chunk(Vec3i origin)
    {
        Origin = origin;

        // Fill with empty tiles
        for (int x = 0; x < Width;  x++)
        for (int y = 0; y < Height; y++)
        for (int z = 0; z < Depth;  z++)
            _tiles[x, y, z] = TileData.Empty;
    }

    // ── Tile access ────────────────────────────────────────────────────────

    /// <summary>Get a tile by local chunk coordinates.</summary>
    public TileData Get(int lx, int ly, int lz) => _tiles[lx, ly, lz];

    /// <summary>Set a tile by local chunk coordinates and mark the chunk dirty.</summary>
    public void Set(int lx, int ly, int lz, TileData tile)
    {
        _tiles[lx, ly, lz] = tile;
        IsDirty = true;
    }

    /// <summary>Called after a consumer finishes processing dirty chunk state.</summary>
    public void ClearDirty() => IsDirty = false;

    public static bool IsLocalInBounds(int lx, int ly, int lz)
        => lx >= 0 && lx < Width
        && ly >= 0 && ly < Height
        && lz >= 0 && lz < Depth;
}
