using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.World;
using Godot;

public static class GroundMaterialResolver
{
    private const string GroundSoilTag = "ground_soil";
    private const string GroundGrassTag = "ground_grass";
    private const string GroundSandTag = "ground_sand";
    private const string GroundMudTag = "ground_mud";
    private const string GroundSnowTag = "ground_snow";
    private const string GroundStoneTag = "ground_stone";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> GroundTileDefIdsByTileDefId =
        new(LoadGroundTileDefIdsByTileDefId);

    private static readonly Lazy<IReadOnlyDictionary<string, TagSet>> MaterialTagsByMaterialId =
        new(LoadMaterialTagsByMaterialId);

    public static string? ResolveGroundTileDefIdFromTileDef(string tileDefId)
    {
        return GroundTileDefIdsByTileDefId.Value.TryGetValue(tileDefId, out var groundTileDefId)
            ? groundTileDefId
            : null;
    }

    public static string? ResolveGroundTileDefId(string? materialId, TagSet? materialTags = null)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return null;

        var normalized = materialId.Trim().ToLowerInvariant();
        var resolvedTags = materialTags;
        if (resolvedTags is null)
            MaterialTagsByMaterialId.Value.TryGetValue(normalized, out resolvedTags);

        if (normalized == "wood")
            return null;

        if (normalized is "mud" or "clay_mud" or "silt_mud")
            return TileDefIds.Mud;

        if (normalized is "sand" or "dune_sand")
            return TileDefIds.Sand;

        if (normalized is "snow" or "ice" or "frost")
            return TileDefIds.Snow;

        if (resolvedTags?.Contains("stone") == true)
            return TileDefIds.StoneFloor;

        if (resolvedTags?.Contains("dirt") == true)
        {
            if (normalized.Contains("sand"))
                return TileDefIds.Sand;
            if (normalized.Contains("mud"))
                return TileDefIds.Mud;
            return TileDefIds.Soil;
        }

        if (normalized.Contains("soil") || normalized.Contains("dirt") ||
            normalized.Contains("loam") || normalized.Contains("peat"))
        {
            return TileDefIds.Soil;
        }

        return TileDefIds.StoneFloor;
    }

    private static IReadOnlyDictionary<string, string> LoadGroundTileDefIdsByTileDefId()
    {
        try
        {
            var path = Path.Combine(ClientSimulationFactory.ResolveDataPath(), "ConfigBundle", "tiles.json");
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(path);
            var tileDefs = JsonSerializer.Deserialize<List<ConfigTagsEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            });

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (tileDefs is null)
                return result;

            foreach (var tileDef in tileDefs)
            {
                if (string.IsNullOrWhiteSpace(tileDef.Id) || tileDef.Tags is null)
                    continue;

                if (tileDef.Tags.Any(tag => string.Equals(tag, GroundGrassTag, StringComparison.OrdinalIgnoreCase)))
                    result[tileDef.Id] = TileDefIds.Grass;
                else if (tileDef.Tags.Any(tag => string.Equals(tag, GroundSandTag, StringComparison.OrdinalIgnoreCase)))
                    result[tileDef.Id] = TileDefIds.Sand;
                else if (tileDef.Tags.Any(tag => string.Equals(tag, GroundMudTag, StringComparison.OrdinalIgnoreCase)))
                    result[tileDef.Id] = TileDefIds.Mud;
                else if (tileDef.Tags.Any(tag => string.Equals(tag, GroundSnowTag, StringComparison.OrdinalIgnoreCase)))
                    result[tileDef.Id] = TileDefIds.Snow;
                else if (tileDef.Tags.Any(tag => string.Equals(tag, GroundSoilTag, StringComparison.OrdinalIgnoreCase)))
                    result[tileDef.Id] = TileDefIds.Soil;
                else if (tileDef.Tags.Any(tag => string.Equals(tag, GroundStoneTag, StringComparison.OrdinalIgnoreCase)))
                    result[tileDef.Id] = TileDefIds.StoneFloor;
            }

            return result;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Failed to load tile ground tags: {exception.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyDictionary<string, TagSet> LoadMaterialTagsByMaterialId()
    {
        try
        {
            var path = Path.Combine(ClientSimulationFactory.ResolveDataPath(), "ConfigBundle", "materials.json");
            if (!File.Exists(path))
                return new Dictionary<string, TagSet>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(path);
            var materialDefs = JsonSerializer.Deserialize<List<ConfigTagsEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            });

            var result = new Dictionary<string, TagSet>(StringComparer.OrdinalIgnoreCase);
            if (materialDefs is null)
                return result;

            foreach (var materialDef in materialDefs)
            {
                if (string.IsNullOrWhiteSpace(materialDef.Id) || materialDef.Tags is null)
                    continue;

                result[materialDef.Id] = TagSet.From(materialDef.Tags);
            }

            return result;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Failed to load material tags: {exception.Message}");
            return new Dictionary<string, TagSet>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record ConfigTagsEntry(string Id, string[]? Tags);
}