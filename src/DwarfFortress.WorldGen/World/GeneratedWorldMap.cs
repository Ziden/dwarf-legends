using System;

namespace DwarfFortress.WorldGen.World;

public sealed class GeneratedWorldMap
{
    private readonly GeneratedWorldTile[] _tiles;

    public int Seed { get; }
    public int Width { get; }
    public int Height { get; }

    public GeneratedWorldMap(int seed, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Seed = seed;
        Width = width;
        Height = height;
        _tiles = new GeneratedWorldTile[width * height];
        Array.Fill(_tiles, GeneratedWorldTile.Empty);
    }

    public GeneratedWorldTile GetTile(int x, int y)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"World tile ({x}, {y}) is out of bounds.");
        return _tiles[IndexOf(x, y)];
    }

    public void SetTile(int x, int y, GeneratedWorldTile tile)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"World tile ({x}, {y}) is out of bounds.");
        _tiles[IndexOf(x, y)] = tile;
    }

    private bool IsInBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    private int IndexOf(int x, int y)
        => y * Width + x;
}
