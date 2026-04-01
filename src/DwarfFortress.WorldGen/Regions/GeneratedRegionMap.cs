using System;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;

namespace DwarfFortress.WorldGen.Regions;

public sealed class GeneratedRegionMap
{
    private readonly GeneratedRegionTile[] _tiles;

    public int Seed { get; }
    public int Width { get; }
    public int Height { get; }
    public WorldCoord WorldCoord { get; }
    public string ParentMacroBiomeId { get; }
    public float ParentForestCover { get; }
    public float ParentMountainCover { get; }
    public float ParentRelief { get; }
    public float ParentMoistureBand { get; }
    public float ParentTemperatureBand { get; }
    public bool ParentHasRiver { get; }
    public byte ParentRiverOrder { get; }
    public float ParentRiverDischarge { get; }

    public GeneratedRegionMap(
        int seed,
        int width,
        int height,
        WorldCoord worldCoord,
        string parentMacroBiomeId = MacroBiomeIds.TemperatePlains,
        float parentForestCover = 0.5f,
        float parentMountainCover = 0f,
        float parentRelief = 0.5f,
        float parentMoistureBand = 0.5f,
        float parentTemperatureBand = 0.5f,
        bool parentHasRiver = false,
        byte parentRiverOrder = 0,
        float parentRiverDischarge = 0f)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Seed = seed;
        Width = width;
        Height = height;
        WorldCoord = worldCoord;
        ParentMacroBiomeId = string.IsNullOrWhiteSpace(parentMacroBiomeId)
            ? MacroBiomeIds.TemperatePlains
            : parentMacroBiomeId;
        ParentForestCover = Math.Clamp(parentForestCover, 0f, 1f);
        ParentMountainCover = Math.Clamp(parentMountainCover, 0f, 1f);
        ParentRelief = Math.Clamp(parentRelief, 0f, 1f);
        ParentMoistureBand = Math.Clamp(parentMoistureBand, 0f, 1f);
        ParentTemperatureBand = Math.Clamp(parentTemperatureBand, 0f, 1f);
        ParentHasRiver = parentHasRiver;
        ParentRiverOrder = parentRiverOrder;
        ParentRiverDischarge = Math.Max(0f, parentRiverDischarge);
        _tiles = new GeneratedRegionTile[width * height];
        Array.Fill(_tiles, GeneratedRegionTile.Empty);
    }

    public GeneratedRegionTile GetTile(int x, int y)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"Region tile ({x}, {y}) is out of bounds.");
        return _tiles[IndexOf(x, y)];
    }

    public void SetTile(int x, int y, GeneratedRegionTile tile)
    {
        if (!IsInBounds(x, y))
            throw new ArgumentOutOfRangeException($"Region tile ({x}, {y}) is out of bounds.");
        _tiles[IndexOf(x, y)] = tile;
    }

    private bool IsInBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    private int IndexOf(int x, int y)
        => y * Width + x;
}
