namespace DwarfFortress.WorldGen.Maps;

public enum GeneratedFluidType : byte
{
    None = 0,
    Water = 1,
    Magma = 2,
}

public readonly record struct GeneratedTile(
    string TileDefId,
    string? MaterialId,
    bool IsPassable,
    GeneratedFluidType FluidType = GeneratedFluidType.None,
    byte FluidLevel = 0,
    string? OreId = null,
    bool IsAquifer = false,
    string? TreeSpeciesId = null,
    string? PlantDefId = null,
    byte PlantGrowthStage = 0,
    float PlantGrowthProgressSeconds = 0f,
    byte PlantYieldLevel = 0,
    byte PlantSeedLevel = 0)
{
    public static GeneratedTile Empty => new(GeneratedTileDefIds.Empty, null, true);
}

public static class GeneratedTileDefIds
{
    public const string Empty = "empty";
    public const string StoneFloor = "stone_floor";
    public const string StoneBrick = "stone_brick_floor";
    public const string StoneWall = "stone_wall";
    public const string Sand = "sand";
    public const string Mud = "mud";
    public const string Snow = "snow";
    public const string GraniteWall = StoneWall;
    public const string LimestoneWall = StoneWall;
    public const string SandstoneWall = StoneWall;
    public const string BasaltWall = StoneWall;
    public const string ShaleWall = StoneWall;
    public const string SlateWall = StoneWall;
    public const string MarbleWall = StoneWall;
    public const string SoilWall = "soil_wall";
    public const string Staircase = "staircase";
    public const string Tree = "tree";
    public const string Grass = "grass";
    public const string Soil = "soil";
    public const string Water = "water_tile";
    public const string Magma = "magma_tile";
}
