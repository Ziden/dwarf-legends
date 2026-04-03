using DwarfFortress.WorldGen.Config;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;

namespace DwarfFortress.WorldGen.Tests;

public sealed class WorldGenPlantCatalogLoaderTests
{
    [Fact]
    public void LoadFromJson_SupportsGroundAndTreePlantsFromContent()
    {
        const string json =
            """
            [
              {
                "id": "glow_cap",
                "hostKind": "ground",
                "allowedBiomes": ["fungal_grove"],
                "allowedGroundTiles": ["soil", "mud"],
                "minMoisture": 0.55,
                "maxMoisture": 1.00,
                "minTerrain": 0.00,
                "maxTerrain": 0.52,
                "prefersNearWater": true,
                "maxGrowthStage": 2,
                "harvestItemDefId": "glow_cap"
              },
              {
                "id": "hanging_orchard",
                "hostKind": "tree",
                "allowedBiomes": ["fungal_grove"],
                "supportedTreeSpecies": ["fungal_bole"],
                "minMoisture": 0.30,
                "maxMoisture": 0.88,
                "minTerrain": 0.00,
                "maxTerrain": 0.60,
                "prefersNearWater": true,
                "maxGrowthStage": 3,
                "fruitItemDefId": "spore_cluster"
              }
            ]
            """;

        var catalog = WorldGenPlantCatalogLoader.LoadFromJson(json);

        var groundResolved = catalog.TryResolveBestGroundPlant(
            "fungal_grove",
            GeneratedTileDefIds.Mud,
            moisture: 0.76f,
            terrain: 0.18f,
            riparianBoost: 0.85f,
            out var groundPlant,
            out var groundScore);

        var treeResolved = catalog.TryResolveBestTreeCanopyPlant(
            "fungal_grove",
            "fungal_bole",
            moisture: 0.58f,
            terrain: 0.24f,
            riparianBoost: 0.82f,
            out var treePlant,
            out var treeScore);

        Assert.True(groundResolved);
        Assert.NotNull(groundPlant);
        Assert.Equal("glow_cap", groundPlant!.Id);
        Assert.True(groundScore > 0.5f);

        Assert.True(treeResolved);
        Assert.NotNull(treePlant);
        Assert.Equal("hanging_orchard", treePlant!.Id);
        Assert.True(treeScore > 0.5f);
    }

    [Fact]
    public void LoadDefaultOrFallback_ContainsBuiltInGroundAndCanopyPlants()
    {
        var catalog = WorldGenPlantCatalogLoader.LoadDefaultOrFallback();

        var marshResolved = catalog.TryResolveBestGroundPlant(
            MacroBiomeIds.MistyMarsh,
            GeneratedTileDefIds.Mud,
            moisture: 0.82f,
            terrain: 0.18f,
            riparianBoost: 0.92f,
            out var marshPlant,
            out _);

        var orchardResolved = catalog.TryResolveBestTreeCanopyPlant(
            MacroBiomeIds.TemperatePlains,
            TreeSpeciesIds.Apple,
            moisture: 0.52f,
            terrain: 0.22f,
            riparianBoost: 0.78f,
            out var canopyPlant,
            out _);

        Assert.True(marshResolved);
        Assert.Equal(PlantSpeciesIds.MarshReed, marshPlant!.Id);
        Assert.True(orchardResolved);
        Assert.Equal(PlantSpeciesIds.AppleCanopy, canopyPlant!.Id);
    }
}