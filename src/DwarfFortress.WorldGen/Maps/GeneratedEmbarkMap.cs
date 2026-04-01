using System;
using System.Collections.Generic;
using DwarfFortress.WorldGen.Creatures;

namespace DwarfFortress.WorldGen.Maps;

public sealed class GeneratedEmbarkMap
{
    private readonly GeneratedTile[] _tiles;
    private readonly List<CreatureSpawn> _creatureSpawns = [];

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public IReadOnlyList<CreatureSpawn> CreatureSpawns => _creatureSpawns;

    public GeneratedEmbarkMap(int width, int height, int depth)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));

        Width = width;
        Height = height;
        Depth = depth;
        _tiles = new GeneratedTile[width * height * depth];
        Array.Fill(_tiles, GeneratedTile.Empty);
    }

    public GeneratedTile GetTile(int x, int y, int z)
    {
        if (!IsInBounds(x, y, z))
            throw new ArgumentOutOfRangeException($"Tile ({x}, {y}, {z}) is out of bounds.");
        return _tiles[IndexOf(x, y, z)];
    }

    public void SetTile(int x, int y, int z, GeneratedTile tile)
    {
        if (!IsInBounds(x, y, z))
            throw new ArgumentOutOfRangeException($"Tile ({x}, {y}, {z}) is out of bounds.");
        _tiles[IndexOf(x, y, z)] = tile;
    }

    public void AddCreatureSpawn(CreatureSpawn spawn)
    {
        if (!IsInBounds(spawn.X, spawn.Y, spawn.Z))
            throw new ArgumentOutOfRangeException(nameof(spawn), $"Creature spawn ({spawn.X}, {spawn.Y}, {spawn.Z}) is out of bounds.");

        _creatureSpawns.Add(spawn);
    }

    private bool IsInBounds(int x, int y, int z)
        => x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;

    private int IndexOf(int x, int y, int z)
        => z * Width * Height + y * Width + x;
}
