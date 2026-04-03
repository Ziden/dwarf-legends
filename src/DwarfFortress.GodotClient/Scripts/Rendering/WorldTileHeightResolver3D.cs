using DwarfFortress.GameLogic.World;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

public static class WorldTileHeightResolver3D
{
    private const float GroundHeight = 0.18f;
    private const float FloorHeight = 0.22f;
    private const float RampHeight = 0.30f;
    private const float StairHeight = 0.34f;
    private const float WallHeight = 0.72f;
    private const float WaterBaseHeight = 0.05f;
    private const float WaterHeightScale = 0.16f;
    private const float MagmaBaseHeight = 0.08f;
    private const float MagmaHeightScale = 0.20f;

    public static float ResolveTileTopHeight(WorldTileData tile)
    {
        if (tile.TileDefId == TileDefIds.Tree)
            return GroundHeight;

        if (IsWaterTile(tile))
            return WaterBaseHeight + ((tile.FluidLevel / 7f) * WaterHeightScale);

        if (IsMagmaTile(tile))
            return MagmaBaseHeight + ((tile.FluidLevel / 7f) * MagmaHeightScale);

        return tile.TileDefId switch
        {
            TileDefIds.Ramp => RampHeight,
            TileDefIds.Staircase => StairHeight,
            TileDefIds.StoneFloor or TileDefIds.Obsidian or TileDefIds.WoodFloor or TileDefIds.StoneBrick => FloorHeight,
            _ when !tile.IsPassable => WallHeight,
            _ => GroundHeight,
        };
    }

    public static float ResolveSurfaceY(int currentZ, WorldTileData tile, float surfaceOffset = 0f)
        => (currentZ * WorldRender3D.VerticalSliceSpacing) + ResolveTileTopHeight(tile) + surfaceOffset;

    public static float ResolveSliceY(int currentZ, float surfaceOffset = 0f)
        => (currentZ * WorldRender3D.VerticalSliceSpacing) + surfaceOffset;

    private static bool IsWaterTile(WorldTileData tile)
        => tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water;

    private static bool IsMagmaTile(WorldTileData tile)
        => tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma;
}
