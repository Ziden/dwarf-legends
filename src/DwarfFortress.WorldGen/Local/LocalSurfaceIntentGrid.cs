namespace DwarfFortress.WorldGen.Local;

public readonly record struct LocalSurfaceIntentGrid(
    string NorthWestTileDefId,
    string NorthTileDefId,
    string NorthEastTileDefId,
    string WestTileDefId,
    string CenterTileDefId,
    string EastTileDefId,
    string SouthWestTileDefId,
    string SouthTileDefId,
    string SouthEastTileDefId)
{
    public string GetTileDefId(int offsetX, int offsetY)
    {
        return (offsetX, offsetY) switch
        {
            (-1, -1) => NorthWestTileDefId,
            (0, -1) => NorthTileDefId,
            (1, -1) => NorthEastTileDefId,
            (-1, 0) => WestTileDefId,
            (0, 0) => CenterTileDefId,
            (1, 0) => EastTileDefId,
            (-1, 1) => SouthWestTileDefId,
            (0, 1) => SouthTileDefId,
            (1, 1) => SouthEastTileDefId,
            _ => CenterTileDefId,
        };
    }
}