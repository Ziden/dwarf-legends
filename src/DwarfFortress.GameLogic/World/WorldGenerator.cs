using DwarfFortress.GameLogic.Core;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.GameLogic.World;

/// <summary>
/// GameLogic adapter over the standalone world-generation library.
/// </summary>
public static class WorldGenerator
{
    public static void GenerateEmbark(
        WorldMap map,
        int width = 48,
        int height = 48,
        int depth = 8,
        int seed = 0,
        string? biomeId = null)
    {
        var settings = new LocalGenerationSettings(width, height, depth, biomeId);
        var generated = EmbarkGenerator.Generate(settings, seed);
        ApplyGeneratedEmbark(map, generated);
    }

    public static void ApplyGeneratedEmbark(WorldMap map, GeneratedEmbarkMap generated)
    {
        map.SetDimensions(generated.Width, generated.Height, generated.Depth);

        for (var x = 0; x < generated.Width; x++)
        for (var y = 0; y < generated.Height; y++)
        for (var z = 0; z < generated.Depth; z++)
        {
            var tile = generated.GetTile(x, y, z);
            map.SetTile(new Vec3i(x, y, z), ToTileData(tile));
        }
    }

    public static TileData ToTileData(GeneratedTile tile) => new()
    {
        TileDefId = tile.TileDefId,
        MaterialId = tile.MaterialId,
        TreeSpeciesId = tile.TreeSpeciesId,
        PlantDefId = tile.PlantDefId,
        PlantGrowthStage = tile.PlantGrowthStage,
        PlantGrowthProgressSeconds = tile.PlantGrowthProgressSeconds,
        PlantYieldLevel = tile.PlantYieldLevel,
        PlantSeedLevel = tile.PlantSeedLevel,
        OreItemDefId = tile.OreId,
        IsAquifer = tile.IsAquifer,
        IsPassable = tile.IsPassable,
        FluidType = tile.FluidType switch
        {
            GeneratedFluidType.Water => FluidType.Water,
            GeneratedFluidType.Magma => FluidType.Magma,
            _ => FluidType.None,
        },
        FluidLevel = tile.FluidLevel,
    };
}
