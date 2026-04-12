using System;
using System.Collections.Generic;
using System.Linq;

namespace DwarfFortress.WorldGen.Config;

public enum WorldGenPlantHostKind : byte
{
    Ground = 0,
    Tree = 1,
}

public sealed record WorldGenPlantDefinition(
    string Id,
    WorldGenPlantHostKind HostKind,
    IReadOnlyList<string> AllowedBiomeIds,
    IReadOnlyList<string> AllowedGroundTileDefIds,
    IReadOnlyList<string> SupportedTreeSpeciesIds,
    float MinMoisture,
    float MaxMoisture,
    float MinTerrain,
    float MaxTerrain,
    bool PrefersNearWater,
    bool PrefersFarFromWater,
    byte MaxGrowthStage,
    string? HarvestItemDefId,
    string? FruitItemDefId,
    bool DropYieldOnHostRemoval = false);

public sealed class WorldGenPlantCatalog
{
    private readonly IReadOnlyList<WorldGenPlantDefinition> _groundPlants;
    private readonly IReadOnlyList<WorldGenPlantDefinition> _treePlants;

    private WorldGenPlantCatalog(
        IReadOnlyList<WorldGenPlantDefinition> groundPlants,
        IReadOnlyList<WorldGenPlantDefinition> treePlants)
    {
        _groundPlants = groundPlants;
        _treePlants = treePlants;
    }

    public static WorldGenPlantCatalog FromDefinitions(IEnumerable<WorldGenPlantDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var all = definitions
            .Where(def => !string.IsNullOrWhiteSpace(def.Id))
            .ToArray();

        return new WorldGenPlantCatalog(
            all.Where(def => def.HostKind == WorldGenPlantHostKind.Ground).ToArray(),
            all.Where(def => def.HostKind == WorldGenPlantHostKind.Tree).ToArray());
    }

    public bool TryResolveBestGroundPlant(
        string biomeId,
        string tileDefId,
        float moisture,
        float terrain,
        float riparianBoost,
        out WorldGenPlantDefinition? definition,
        out float bestScore)
    {
        bestScore = 0f;
        definition = null;

        foreach (var candidate in _groundPlants)
        {
            var score = ScoreGroundPlant(candidate, biomeId, tileDefId, moisture, terrain, riparianBoost);
            if (score <= bestScore)
                continue;

            bestScore = score;
            definition = candidate;
        }

        return definition is not null;
    }

    public bool TryResolveBestTreeCanopyPlant(
        string biomeId,
        string treeSpeciesId,
        float moisture,
        float terrain,
        float riparianBoost,
        out WorldGenPlantDefinition? definition,
        out float bestScore)
    {
        bestScore = 0f;
        definition = null;

        foreach (var candidate in _treePlants)
        {
            var score = ScoreTreePlant(candidate, biomeId, treeSpeciesId, moisture, terrain, riparianBoost);
            if (score <= bestScore)
                continue;

            bestScore = score;
            definition = candidate;
        }

        return definition is not null;
    }

    private static float ScoreGroundPlant(
        WorldGenPlantDefinition definition,
        string biomeId,
        string tileDefId,
        float moisture,
        float terrain,
        float riparianBoost)
    {
        if (!SupportsBiome(definition, biomeId) || !SupportsGroundTile(definition, tileDefId))
            return 0f;

        var moistureFit = ResolveRangeFit(moisture, definition.MinMoisture, definition.MaxMoisture);
        var terrainFit = ResolveRangeFit(terrain, definition.MinTerrain, definition.MaxTerrain);
        if (moistureFit <= 0f || terrainFit <= 0f)
            return 0f;

        var waterFit = ResolveWaterFit(definition, riparianBoost);
        return Math.Clamp((moistureFit * 0.42f) + (terrainFit * 0.28f) + (waterFit * 0.20f) + 0.10f, 0f, 1f);
    }

    private static float ScoreTreePlant(
        WorldGenPlantDefinition definition,
        string biomeId,
        string treeSpeciesId,
        float moisture,
        float terrain,
        float riparianBoost)
    {
        if (!SupportsBiome(definition, biomeId) || !SupportsTreeSpecies(definition, treeSpeciesId))
            return 0f;

        var moistureFit = ResolveRangeFit(moisture, definition.MinMoisture, definition.MaxMoisture);
        var terrainFit = ResolveRangeFit(terrain, definition.MinTerrain, definition.MaxTerrain);
        if (moistureFit <= 0f || terrainFit <= 0f)
            return 0f;

        var waterFit = ResolveWaterFit(definition, riparianBoost);
        return Math.Clamp((moistureFit * 0.40f) + (terrainFit * 0.24f) + (waterFit * 0.20f) + 0.16f, 0f, 1f);
    }

    private static bool SupportsBiome(WorldGenPlantDefinition definition, string biomeId)
        => definition.AllowedBiomeIds.Count == 0 || definition.AllowedBiomeIds.Contains(biomeId, StringComparer.OrdinalIgnoreCase);

    private static bool SupportsGroundTile(WorldGenPlantDefinition definition, string tileDefId)
        => definition.AllowedGroundTileDefIds.Count == 0 || definition.AllowedGroundTileDefIds.Contains(tileDefId, StringComparer.OrdinalIgnoreCase);

    private static bool SupportsTreeSpecies(WorldGenPlantDefinition definition, string treeSpeciesId)
        => definition.SupportedTreeSpeciesIds.Count == 0 || definition.SupportedTreeSpeciesIds.Contains(treeSpeciesId, StringComparer.OrdinalIgnoreCase);

    private static float ResolveRangeFit(float value, float min, float max)
    {
        if (value < min || value > max)
            return 0f;

        var span = Math.Max(0.001f, max - min);
        var halfSpan = span * 0.5f;
        var center = min + halfSpan;
        var normalized = halfSpan <= 0.0005f
            ? 1f
            : 1f - MathF.Abs(value - center) / halfSpan;

        return 0.45f + (Math.Clamp(normalized, 0f, 1f) * 0.55f);
    }

    private static float ResolveWaterFit(WorldGenPlantDefinition definition, float riparianBoost)
    {
        var clamped = Math.Clamp(riparianBoost, 0f, 1f);
        if (definition.PrefersNearWater)
            return 0.25f + (clamped * 0.75f);
        if (definition.PrefersFarFromWater)
            return 0.25f + ((1f - clamped) * 0.75f);

        return 0.65f;
    }
}