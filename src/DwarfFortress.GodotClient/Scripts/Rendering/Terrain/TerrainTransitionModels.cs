using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct TileTransitionSet<T>(
    T? North,
    T? South,
    T? West,
    T? East,
    T? NorthWest,
    T? NorthEast,
    T? SouthWest,
    T? SouthEast)
    where T : struct
{
    public bool HasNorth => North is not null;
    public bool HasSouth => South is not null;
    public bool HasWest => West is not null;
    public bool HasEast => East is not null;
    public bool HasNorthWest => NorthWest is not null;
    public bool HasNorthEast => NorthEast is not null;
    public bool HasSouthWest => SouthWest is not null;
    public bool HasSouthEast => SouthEast is not null;
}

public readonly record struct CardinalNeighborSet<T>(T? North, T? South, T? West, T? East)
    where T : struct
{
    public bool HasNorth => North is not null;
    public bool HasSouth => South is not null;
    public bool HasWest => West is not null;
    public bool HasEast => East is not null;
}

public readonly record struct CornerNeighborSet<T>(T? NorthWest, T? NorthEast, T? SouthWest, T? SouthEast)
    where T : struct
{
    public bool HasNorthWest => NorthWest is not null;
    public bool HasNorthEast => NorthEast is not null;
    public bool HasSouthWest => SouthWest is not null;
    public bool HasSouthEast => SouthEast is not null;
}

public readonly record struct CardinalNeighborMatchSet(bool NorthMatch, bool SouthMatch, bool WestMatch, bool EastMatch);

public readonly record struct GroundVisualData(string TileDefId, string? MaterialId)
{
    public bool IsNaturalBlendTile
        => TerrainGroundResolver.IsNaturalBlendTileDefId(TileDefId);
}

public readonly record struct TerrainTransitionSet(
    GroundVisualData? North,
    GroundVisualData? South,
    GroundVisualData? West,
    GroundVisualData? East,
    GroundVisualData? NorthWest,
    GroundVisualData? NorthEast,
    GroundVisualData? SouthWest,
    GroundVisualData? SouthEast)
{
    public bool HasNorth => North is not null;
    public bool HasSouth => South is not null;
    public bool HasWest => West is not null;
    public bool HasEast => East is not null;
    public bool HasNorthWest => NorthWest is not null;
    public bool HasNorthEast => NorthEast is not null;
    public bool HasSouthWest => SouthWest is not null;
    public bool HasSouthEast => SouthEast is not null;

    public static TerrainTransitionSet FromTileTransitions(in TileTransitionSet<GroundVisualData> transitions)
        => new(
            transitions.North,
            transitions.South,
            transitions.West,
            transitions.East,
            transitions.NorthWest,
            transitions.NorthEast,
            transitions.SouthWest,
            transitions.SouthEast);
}

public enum TerrainEdgeDirection : byte
{
    North = 0,
    South = 1,
    West = 2,
    East = 3,
}

public enum TerrainCornerQuadrant : byte
{
    NorthWest = 0,
    NorthEast = 1,
    SouthWest = 2,
    SouthEast = 3,
}
