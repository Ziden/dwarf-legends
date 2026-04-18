namespace DwarfFortress.GameLogic.World;

/// <summary>
/// Serializable snapshot of a tile's mutable runtime state.
/// </summary>
public sealed class TileDataSnapshot
{
    public string TileDefId { get; set; } = TileDefIds.Empty;
    public string? MaterialId { get; set; }
    public string? TreeSpeciesId { get; set; }
    public string? PlantDefId { get; set; }
    public byte PlantGrowthStage { get; set; }
    public float PlantGrowthProgressSeconds { get; set; }
    public byte PlantYieldLevel { get; set; }
    public byte PlantSeedLevel { get; set; }
    public string? OreItemDefId { get; set; }
    public bool IsAquifer { get; set; }
    public byte FluidType { get; set; }
    public byte FluidLevel { get; set; }
    public string? FluidMaterialId { get; set; }
    public string? CoatingMaterialId { get; set; }
    public float CoatingAmount { get; set; }
    public bool IsDesignated { get; set; }
    public bool IsUnderConstruction { get; set; }
    public bool IsPassable { get; set; }

    public static TileDataSnapshot FromTile(TileData tile)
        => new()
        {
            TileDefId = tile.TileDefId,
            MaterialId = tile.MaterialId,
            TreeSpeciesId = tile.TreeSpeciesId,
            PlantDefId = tile.PlantDefId,
            PlantGrowthStage = tile.PlantGrowthStage,
            PlantGrowthProgressSeconds = tile.PlantGrowthProgressSeconds,
            PlantYieldLevel = tile.PlantYieldLevel,
            PlantSeedLevel = tile.PlantSeedLevel,
            OreItemDefId = tile.OreItemDefId,
            IsAquifer = tile.IsAquifer,
            FluidType = (byte)tile.FluidType,
            FluidLevel = tile.FluidLevel,
            FluidMaterialId = tile.FluidMaterialId,
            CoatingMaterialId = tile.CoatingMaterialId,
            CoatingAmount = tile.CoatingAmount,
            IsDesignated = tile.IsDesignated,
            IsUnderConstruction = tile.IsUnderConstruction,
            IsPassable = tile.IsPassable,
        };

    public TileData ToTileData()
        => new()
        {
            TileDefId = TileDefId,
            MaterialId = MaterialId,
            TreeSpeciesId = TreeSpeciesId,
            PlantDefId = PlantDefId,
            PlantGrowthStage = PlantGrowthStage,
            PlantGrowthProgressSeconds = PlantGrowthProgressSeconds,
            PlantYieldLevel = PlantYieldLevel,
            PlantSeedLevel = PlantSeedLevel,
            OreItemDefId = OreItemDefId,
            IsAquifer = IsAquifer,
            FluidType = (FluidType)FluidType,
            FluidLevel = FluidLevel,
            FluidMaterialId = FluidMaterialId,
            CoatingMaterialId = CoatingMaterialId,
            CoatingAmount = CoatingAmount,
            IsDesignated = IsDesignated,
            IsUnderConstruction = IsUnderConstruction,
            IsPassable = IsPassable,
        };
}
