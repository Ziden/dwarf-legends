using System;
using System.IO;
using System.Text.Json;

namespace DwarfFortress.WorldGen.Config;

public static class WorldGenContentConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WorldGenContentConfig LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Config path cannot be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Worldgen content config file not found: {path}", path);

        return LoadFromJson(File.ReadAllText(path));
    }

    public static WorldGenContentConfig LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Config JSON cannot be empty.", nameof(json));

        var parsed = JsonSerializer.Deserialize<WorldGenContentConfig>(json, JsonOptions);
        if (parsed is null)
            throw new InvalidOperationException("Failed to parse worldgen content config.");

        return parsed;
    }

    public static WorldGenContentConfig LoadDefaultOrFallback()
    {
      foreach (var candidate in WorldGenConfigPathResolver.EnumerateCandidatePaths("data/ConfigBundle/worldgen/worldgen_content.json"))
        {
            if (!File.Exists(candidate))
                continue;

            try
            {
                return LoadFromFile(candidate);
            }
            catch
            {
            }
        }

        return LoadFromJson(DefaultConfigJson);
    }

    private const string DefaultConfigJson =
        """
        {
          "geologyProfiles": [
            {
              "id": "mixed_bedrock",
              "seedSalt": 101,
              "aquiferDepthFraction": 0.18,
              "layers": [
                { "rockTypeId": "sandstone", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "limestone", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "slate", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "granite", "thicknessMin": 3, "thicknessMax": 5 }
              ],
              "mineralVeins": [
                { "materialId": "iron", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.32, "requiredRockTypeId": "granite", "sizeMin": 10, "sizeMax": 24 },
                { "materialId": "copper", "resourceFormRole": "ore", "shape": "Cluster", "frequency": 0.24, "requiredRockTypeId": "limestone", "sizeMin": 8, "sizeMax": 20 },
                { "materialId": "coal", "resourceFormRole": "ore", "shape": "Layer", "frequency": 0.28, "requiredRockTypeId": "sandstone", "sizeMin": 18, "sizeMax": 36 },
                { "materialId": "tin", "resourceFormRole": "ore", "shape": "Scattered", "frequency": 0.18, "requiredRockTypeId": "slate", "sizeMin": 8, "sizeMax": 18 },
                { "materialId": "silver", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.12, "requiredRockTypeId": "granite", "sizeMin": 8, "sizeMax": 18 }
              ]
            },
            {
              "id": "igneous_uplift",
              "seedSalt": 211,
              "aquiferDepthFraction": 0.0,
              "layers": [
                { "rockTypeId": "basalt", "thicknessMin": 3, "thicknessMax": 5 },
                { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 4 },
                { "rockTypeId": "basalt", "thicknessMin": 1, "thicknessMax": 2 }
              ],
              "mineralVeins": [
                { "materialId": "iron", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.44, "requiredRockTypeId": "granite", "sizeMin": 14, "sizeMax": 28 },
                { "materialId": "iron", "resourceFormRole": "ore", "shape": "Cluster", "frequency": 0.20, "requiredRockTypeId": "basalt", "sizeMin": 10, "sizeMax": 24 },
                { "materialId": "copper", "resourceFormRole": "ore", "shape": "Scattered", "frequency": 0.16, "requiredRockTypeId": "basalt", "sizeMin": 8, "sizeMax": 16 },
                { "materialId": "gold", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.10, "requiredRockTypeId": "basalt", "sizeMin": 8, "sizeMax": 16 }
              ]
            },
            {
              "id": "sedimentary_wetlands",
              "seedSalt": 307,
              "aquiferDepthFraction": 0.22,
              "layers": [
                { "rockTypeId": "sandstone", "thicknessMin": 3, "thicknessMax": 5 },
                { "rockTypeId": "shale", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "limestone", "thicknessMin": 2, "thicknessMax": 4 }
              ],
              "mineralVeins": [
                { "materialId": "coal", "resourceFormRole": "ore", "shape": "Layer", "frequency": 0.42, "requiredRockTypeId": "sandstone", "sizeMin": 20, "sizeMax": 40 },
                { "materialId": "coal", "resourceFormRole": "ore", "shape": "Layer", "frequency": 0.24, "requiredRockTypeId": "shale", "sizeMin": 18, "sizeMax": 30 },
                { "materialId": "copper", "resourceFormRole": "ore", "shape": "Cluster", "frequency": 0.20, "requiredRockTypeId": "limestone", "sizeMin": 8, "sizeMax": 18 },
                { "materialId": "tin", "resourceFormRole": "ore", "shape": "Scattered", "frequency": 0.16, "requiredRockTypeId": "shale", "sizeMin": 8, "sizeMax": 18 }
              ]
            },
            {
              "id": "alluvial_basin",
              "seedSalt": 401,
              "aquiferDepthFraction": 0.26,
              "layers": [
                { "rockTypeId": "sandstone", "thicknessMin": 2, "thicknessMax": 4 },
                { "rockTypeId": "limestone", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "shale", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "granite", "thicknessMin": 2, "thicknessMax": 4 }
              ],
              "mineralVeins": [
                { "materialId": "coal", "resourceFormRole": "ore", "shape": "Layer", "frequency": 0.36, "requiredRockTypeId": "sandstone", "sizeMin": 18, "sizeMax": 36 },
                { "materialId": "copper", "resourceFormRole": "ore", "shape": "Cluster", "frequency": 0.26, "requiredRockTypeId": "limestone", "sizeMin": 10, "sizeMax": 24 },
                { "materialId": "tin", "resourceFormRole": "ore", "shape": "Scattered", "frequency": 0.24, "requiredRockTypeId": "shale", "sizeMin": 10, "sizeMax": 20 },
                { "materialId": "iron", "resourceFormRole": "ore", "shape": "Scattered", "frequency": 0.18, "requiredRockTypeId": "granite", "sizeMin": 10, "sizeMax": 22 },
                { "materialId": "silver", "resourceFormRole": "ore", "shape": "Cluster", "frequency": 0.10, "requiredRockTypeId": "limestone", "sizeMin": 8, "sizeMax": 14 }
              ]
            },
            {
              "id": "metamorphic_spine",
              "seedSalt": 503,
              "aquiferDepthFraction": 0.10,
              "layers": [
                { "rockTypeId": "marble", "thicknessMin": 2, "thicknessMax": 3 },
                { "rockTypeId": "slate", "thicknessMin": 3, "thicknessMax": 5 },
                { "rockTypeId": "granite", "thicknessMin": 1, "thicknessMax": 2 }
              ],
              "mineralVeins": [
                { "materialId": "iron", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.40, "requiredRockTypeId": "granite", "sizeMin": 14, "sizeMax": 30 },
                { "materialId": "copper", "resourceFormRole": "ore", "shape": "Scattered", "frequency": 0.16, "requiredRockTypeId": "slate", "sizeMin": 10, "sizeMax": 20 },
                { "materialId": "coal", "resourceFormRole": "ore", "shape": "Layer", "frequency": 0.12, "requiredRockTypeId": "slate", "sizeMin": 12, "sizeMax": 24 },
                { "materialId": "silver", "resourceFormRole": "ore", "shape": "Vein", "frequency": 0.24, "requiredRockTypeId": "marble", "sizeMin": 10, "sizeMax": 20 },
                { "materialId": "gold", "resourceFormRole": "ore", "shape": "Cluster", "frequency": 0.10, "requiredRockTypeId": "marble", "sizeMin": 8, "sizeMax": 16 }
              ]
            }
          ],
          "treeProfiles": [
            {
              "biomeId": "temperate_plains",
              "subsurfaceMaterialId": "soil",
              "rules": [
                {
                  "minRiparianBoost": 0.85,
                  "species": [
                    { "speciesId": "willow", "weight": 0.58 },
                    { "speciesId": "birch", "weight": 0.42 }
                  ]
                },
                {
                  "minMoisture": 0.26,
                  "maxMoisture": 0.72,
                  "maxTerrain": 0.62,
                  "chance": 0.18,
                  "species": [
                    { "speciesId": "apple", "weight": 1.0 }
                  ]
                }
              ],
              "defaultSpecies": [
                { "speciesId": "oak", "weight": 0.64 },
                { "speciesId": "birch", "weight": 0.36 }
              ]
            },
            {
              "biomeId": "conifer_forest",
              "subsurfaceMaterialId": "soil",
              "defaultSpecies": [
                { "speciesId": "spruce", "weight": 0.54 },
                { "speciesId": "pine", "weight": 0.46 }
              ]
            },
            {
              "biomeId": "highland",
              "subsurfaceMaterialId": "soil",
              "defaultSpecies": [
                { "speciesId": "deadwood", "weight": 1.0 }
              ]
            },
            {
              "biomeId": "misty_marsh",
              "subsurfaceMaterialId": "mud",
              "defaultSpecies": [
                { "speciesId": "willow", "weight": 1.0 }
              ]
            },
            {
              "biomeId": "windswept_steppe",
              "subsurfaceMaterialId": "sand",
              "rules": [
                {
                  "minRiparianBoost": 0.80,
                  "species": [
                    { "speciesId": "birch", "weight": 1.0 }
                  ]
                },
                {
                  "minMoisture": 0.45,
                  "maxTerrain": 0.55,
                  "species": [
                    { "speciesId": "birch", "weight": 1.0 }
                  ]
                }
              ],
              "defaultSpecies": [
                { "speciesId": "deadwood", "weight": 1.0 }
              ]
            },
            {
              "biomeId": "tropical_rainforest",
              "subsurfaceMaterialId": "soil",
              "defaultSpecies": [
                { "speciesId": "palm", "weight": 0.62 },
                { "speciesId": "baobab", "weight": 0.38 }
              ]
            },
            {
              "biomeId": "savanna",
              "subsurfaceMaterialId": "sand",
              "defaultSpecies": [
                { "speciesId": "baobab", "weight": 0.70 },
                { "speciesId": "palm", "weight": 0.30 }
              ]
            },
            {
              "biomeId": "desert",
              "subsurfaceMaterialId": "sand",
              "defaultSpecies": [
                { "speciesId": "deadwood", "weight": 1.0 }
              ]
            },
            {
              "biomeId": "tundra",
              "subsurfaceMaterialId": "soil",
              "defaultSpecies": [
                { "speciesId": "deadwood", "weight": 1.0 }
              ]
            },
            {
              "biomeId": "boreal_forest",
              "subsurfaceMaterialId": "soil",
              "defaultSpecies": [
                { "speciesId": "spruce", "weight": 0.54 },
                { "speciesId": "pine", "weight": 0.46 }
              ]
            },
            {
              "biomeId": "ice_plains",
              "subsurfaceMaterialId": "soil",
              "defaultSpecies": [
                { "speciesId": "deadwood", "weight": 1.0 }
              ]
            }
          ]
        }
        """;
}
