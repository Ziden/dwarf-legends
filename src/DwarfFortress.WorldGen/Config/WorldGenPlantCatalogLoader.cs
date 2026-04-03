using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.WorldGen.Config;

public static class WorldGenPlantCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WorldGenPlantCatalog LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Config path cannot be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Plant config file not found: {path}", path);

        return LoadFromJson(File.ReadAllText(path));
    }

    public static WorldGenPlantCatalog LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Config JSON cannot be empty.", nameof(json));

        var parsed = JsonSerializer.Deserialize<List<WorldGenPlantDefinitionConfig>>(json, JsonOptions);
        if (parsed is null)
            throw new InvalidOperationException("Failed to parse worldgen plant config.");

        return WorldGenPlantCatalog.FromDefinitions(parsed.Select(MapDefinition));
    }

    public static WorldGenPlantCatalog LoadDefaultOrFallback()
    {
        var shared = SharedContentCatalogLoader.LoadDefaultOrFallback();
        if (shared.Plants.Count > 0)
            return FromSharedContent(shared);

        return LoadFromJson(FallbackJson);
    }

    public static WorldGenPlantCatalog FromSharedContent(SharedContentCatalog catalog)
        => WorldGenPlantCatalog.FromDefinitions(catalog.Plants.Values.Select(plant => new WorldGenPlantDefinition(
            Id: plant.Id,
            HostKind: plant.HostKind == PlantContentHostKind.Tree ? WorldGenPlantHostKind.Tree : WorldGenPlantHostKind.Ground,
            AllowedBiomeIds: plant.AllowedBiomeIds,
            AllowedGroundTileDefIds: plant.AllowedGroundTileDefIds,
            SupportedTreeSpeciesIds: plant.SupportedTreeSpeciesIds,
            MinMoisture: plant.MinMoisture,
            MaxMoisture: plant.MaxMoisture,
            MinTerrain: plant.MinTerrain,
            MaxTerrain: plant.MaxTerrain,
            PrefersNearWater: plant.PrefersNearWater,
            PrefersFarFromWater: plant.PrefersFarFromWater,
            MaxGrowthStage: plant.MaxGrowthStage,
            HarvestItemDefId: plant.HarvestItemDefId,
            FruitItemDefId: plant.FruitItemDefId)));

    private static WorldGenPlantDefinition MapDefinition(WorldGenPlantDefinitionConfig config)
    {
        return new WorldGenPlantDefinition(
            Id: config.Id,
            HostKind: ParseHostKind(config.HostKind),
            AllowedBiomeIds: config.AllowedBiomes ?? [],
            AllowedGroundTileDefIds: config.AllowedGroundTiles ?? [],
            SupportedTreeSpeciesIds: config.SupportedTreeSpecies ?? [],
            MinMoisture: config.MinMoisture,
            MaxMoisture: config.MaxMoisture,
            MinTerrain: config.MinTerrain,
            MaxTerrain: config.MaxTerrain,
            PrefersNearWater: config.PrefersNearWater,
            PrefersFarFromWater: config.PrefersFarFromWater,
            MaxGrowthStage: config.MaxGrowthStage,
            HarvestItemDefId: config.HarvestItemDefId,
            FruitItemDefId: config.FruitItemDefId);
    }

    private static WorldGenPlantHostKind ParseHostKind(string? hostKind)
    {
        return string.Equals(hostKind, "tree", StringComparison.OrdinalIgnoreCase)
            ? WorldGenPlantHostKind.Tree
            : WorldGenPlantHostKind.Ground;
    }

    private sealed class WorldGenPlantDefinitionConfig
    {
        public string Id { get; init; } = string.Empty;
        public string? HostKind { get; init; }
        public List<string>? AllowedBiomes { get; init; }
        public List<string>? AllowedGroundTiles { get; init; }
        public List<string>? SupportedTreeSpecies { get; init; }
        public float MinMoisture { get; init; }
        public float MaxMoisture { get; init; } = 1f;
        public float MinTerrain { get; init; }
        public float MaxTerrain { get; init; } = 1f;
        public bool PrefersNearWater { get; init; }
        public bool PrefersFarFromWater { get; init; }
        public byte MaxGrowthStage { get; init; } = 3;
        public string? HarvestItemDefId { get; init; }
        public string? FruitItemDefId { get; init; }
    }

    private const string FallbackJson =
        """
        [
          {
            "id": "berry_bush",
            "hostKind": "ground",
            "allowedBiomes": ["temperate_plains", "misty_marsh", "boreal_forest"],
            "allowedGroundTiles": ["grass", "soil", "mud"],
            "minMoisture": 0.42,
            "maxMoisture": 0.95,
            "minTerrain": 0.00,
            "maxTerrain": 0.72,
            "prefersNearWater": true,
            "maxGrowthStage": 3,
            "harvestItemDefId": "berry_cluster",
            "fruitItemDefId": "berry_cluster"
          },
          {
            "id": "sunroot",
            "hostKind": "ground",
            "allowedBiomes": ["temperate_plains", "savanna", "windswept_steppe"],
            "allowedGroundTiles": ["grass", "soil", "sand"],
            "minMoisture": 0.18,
            "maxMoisture": 0.72,
            "minTerrain": 0.00,
            "maxTerrain": 0.78,
            "prefersFarFromWater": true,
            "maxGrowthStage": 3,
            "harvestItemDefId": "sunroot_bulb"
          },
          {
            "id": "stone_tuber",
            "hostKind": "ground",
            "allowedBiomes": ["highland", "windswept_steppe", "tundra"],
            "allowedGroundTiles": ["soil", "grass", "stone_floor"],
            "minMoisture": 0.10,
            "maxMoisture": 0.58,
            "minTerrain": 0.20,
            "maxTerrain": 1.00,
            "maxGrowthStage": 3,
            "harvestItemDefId": "stone_tuber"
          },
          {
            "id": "marsh_reed",
            "hostKind": "ground",
            "allowedBiomes": ["misty_marsh", "tropical_rainforest", "temperate_plains"],
            "allowedGroundTiles": ["mud", "soil", "grass"],
            "minMoisture": 0.58,
            "maxMoisture": 1.00,
            "minTerrain": 0.00,
            "maxTerrain": 0.55,
            "prefersNearWater": true,
            "maxGrowthStage": 3,
            "harvestItemDefId": "marsh_reed_shoot"
          },
          {
            "id": "apple_canopy",
            "hostKind": "tree",
            "allowedBiomes": ["temperate_plains", "boreal_forest"],
            "supportedTreeSpecies": ["apple"],
            "minMoisture": 0.24,
            "maxMoisture": 0.82,
            "minTerrain": 0.00,
            "maxTerrain": 0.70,
            "prefersNearWater": true,
            "maxGrowthStage": 3,
            "harvestItemDefId": "apple",
            "fruitItemDefId": "apple"
          },
          {
            "id": "fig_canopy",
            "hostKind": "tree",
            "allowedBiomes": ["savanna", "desert", "temperate_plains"],
            "supportedTreeSpecies": ["fig"],
            "minMoisture": 0.14,
            "maxMoisture": 0.72,
            "minTerrain": 0.00,
            "maxTerrain": 0.76,
            "prefersFarFromWater": true,
            "maxGrowthStage": 3,
            "harvestItemDefId": "fig",
            "fruitItemDefId": "fig"
          }
        ]
        """;
}
