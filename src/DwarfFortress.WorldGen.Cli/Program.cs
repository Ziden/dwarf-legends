using System.Globalization;
using System.Text.Json;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "generate-map" => RunGenerateMap(options),
                "generate-world" => RunGenerateWorld(options),
                "generate-region" => RunGenerateRegion(options),
                "generate-lore" => RunGenerateLore(options),
                "analyze-depth" => RunAnalyzeDepth(options),
                "analyze-pipeline" => RunAnalyzePipeline(options),
                _ => FailUnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunGenerateMap(Dictionary<string, string> options)
    {
        var seed = GetInt(options, "seed", 0);
        var width = GetInt(options, "width", 48);
        var height = GetInt(options, "height", 48);
        var depth = GetInt(options, "depth", 8);
        var config = LoadLoreConfigOrNull(GetString(options, "config"));
        var biome = GetString(options, "biome");
        if (string.IsNullOrWhiteSpace(biome) && config is not null)
            biome = WorldLoreGenerator.Generate(seed, width, height, depth, config).BiomeId;

        var map = EmbarkGenerator.Generate(width, height, depth, seed, biome);
        var metrics = WorldGenAnalyzer.AnalyzeMap(map);
        var surfaceCounts = CountSurfaceTileDefs(map);

        var payload = new
        {
            command = "generate-map",
            input = new { seed, width, height, depth, biome, config = GetString(options, "config") },
            metrics,
            surfaceTileCounts = surfaceCounts,
        };

        WriteJson(payload, compact: GetBool(options, "compact"));
        return 0;
    }

    private static int RunGenerateWorld(Dictionary<string, string> options)
    {
        var seed = GetInt(options, "seed", 0);
        var width = GetInt(options, "width", 64);
        var height = GetInt(options, "height", 64);

        var gen = new WorldLayerGenerator();
        var world = gen.Generate(seed, width, height);

        var biomeCounts = CountWorldBiomes(world);
        var avgElevation = 0f;
        var avgMoisture = 0f;
        var avgTemperature = 0f;
        var avgDrainage = 0f;
        var avgFactionPressure = 0f;
        var total = world.Width * world.Height;

        for (var x = 0; x < world.Width; x++)
        for (var y = 0; y < world.Height; y++)
        {
            var tile = world.GetTile(x, y);
            avgElevation += tile.ElevationBand;
            avgMoisture += tile.MoistureBand;
            avgTemperature += tile.TemperatureBand;
            avgDrainage += tile.DrainageBand;
            avgFactionPressure += tile.FactionPressure;
        }

        if (total > 0)
        {
            avgElevation /= total;
            avgMoisture /= total;
            avgTemperature /= total;
            avgDrainage /= total;
            avgFactionPressure /= total;
        }

        var payload = new
        {
            command = "generate-world",
            input = new { seed, width, height },
            metrics = new
            {
                avgElevation,
                avgMoisture,
                avgTemperature,
                avgDrainage,
                avgFactionPressure,
                biomeCounts,
            }
        };

        WriteJson(payload, compact: GetBool(options, "compact"));
        return 0;
    }

    private static int RunGenerateRegion(Dictionary<string, string> options)
    {
        var seed = GetInt(options, "seed", 0);
        var worldWidth = GetInt(options, "world-width", 64);
        var worldHeight = GetInt(options, "world-height", 64);
        var wx = GetInt(options, "wx", worldWidth / 2);
        var wy = GetInt(options, "wy", worldHeight / 2);
        var regionWidth = GetInt(options, "region-width", 32);
        var regionHeight = GetInt(options, "region-height", 32);

        var worldGen = new WorldLayerGenerator();
        var regionGen = new RegionLayerGenerator();

        var world = worldGen.Generate(seed, worldWidth, worldHeight);
        var region = regionGen.Generate(world, new WorldCoord(wx, wy), regionWidth, regionHeight);
        var parent = world.GetTile(wx, wy);

        var variantCounts = CountRegionVariants(region);

        var riverTiles = 0;
        var lakeTiles = 0;
        var roadTiles = 0;
        var settlements = 0;
        var avgVegetation = 0f;
        var avgResources = 0f;

        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            var tile = region.GetTile(x, y);
            if (tile.HasRiver) riverTiles++;
            if (tile.HasLake) lakeTiles++;
            if (tile.HasRoad) roadTiles++;
            if (tile.HasSettlement) settlements++;
            avgVegetation += tile.VegetationDensity;
            avgResources += tile.ResourceRichness;
        }

        var total = region.Width * region.Height;
        if (total > 0)
        {
            avgVegetation /= total;
            avgResources /= total;
        }

        var payload = new
        {
            command = "generate-region",
            input = new { seed, worldWidth, worldHeight, wx, wy, regionWidth, regionHeight },
            parentWorldTile = parent,
            metrics = new
            {
                riverTiles,
                lakeTiles,
                roadTiles,
                settlements,
                avgVegetation,
                avgResources,
                biomeVariantCounts = variantCounts,
            }
        };

        WriteJson(payload, compact: GetBool(options, "compact"));
        return 0;
    }

    private static int RunGenerateLore(Dictionary<string, string> options)
    {
        var seed = GetInt(options, "seed", 0);
        var width = GetInt(options, "width", 48);
        var height = GetInt(options, "height", 48);
        var depth = GetInt(options, "depth", 8);
        var historyLimit = GetInt(options, "history", 20);
        var configPath = GetString(options, "config");
        var config = LoadLoreConfigOrNull(configPath);

        var lore = WorldLoreGenerator.Generate(seed, width, height, depth, config);
        var metrics = WorldGenAnalyzer.AnalyzeLore(lore);

        var payload = new
        {
            command = "generate-lore",
            input = new { seed, width, height, depth, history = historyLimit, config = configPath },
            lore = new
            {
                lore.RegionName,
                lore.BiomeId,
                lore.Threat,
                lore.Prosperity,
                lore.SimulatedYears,
                factions = lore.Factions.Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.IsHostile,
                    f.PrimaryUnitDefId,
                    f.Influence,
                    f.Militarism,
                    f.TradeFocus,
                    f.Motto,
                }),
                sites = lore.Sites.Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Kind,
                    s.OwnerFactionId,
                    s.X,
                    s.Y,
                    s.Z,
                    s.Summary,
                    s.Status,
                    s.Development,
                    s.Security,
                }),
                relations = lore.FactionRelations.Select(r => new
                {
                    r.FactionAId,
                    r.FactionBId,
                    r.Score,
                    r.Stance,
                }),
                history = lore.History
                    .OrderByDescending(h => h.Year)
                    .Take(Math.Max(0, historyLimit))
                    .Select(h => new
                    {
                        h.Year,
                        h.Type,
                        h.Summary,
                        h.FactionAId,
                        h.FactionBId,
                        h.SiteId,
                    }),
            },
            metrics,
        };

        WriteJson(payload, compact: GetBool(options, "compact"));
        return 0;
    }

    private static int RunAnalyzeDepth(Dictionary<string, string> options)
    {
        var seedStart = GetInt(options, "seed-start", 0);
        var seedCount = GetInt(options, "seed-count", 50);
        var width = GetInt(options, "width", 48);
        var height = GetInt(options, "height", 48);
        var depth = GetInt(options, "depth", 8);
        var configPath = GetString(options, "config");
        var config = LoadLoreConfigOrNull(configPath);

        var report = WorldGenAnalyzer.AnalyzeDepthSamples(seedStart, seedCount, width, height, depth, config);
        var payload = new
        {
            command = "analyze-depth",
            input = new { seedStart, seedCount, width, height, depth, config = configPath },
            report,
        };

        WriteJson(payload, compact: GetBool(options, "compact"));

        var enforce = GetBool(options, "enforce-budgets");
        if (enforce && !report.Passed)
            return 2;

        return 0;
    }

    private static int RunAnalyzePipeline(Dictionary<string, string> options)
    {
        var seedStart = GetInt(options, "seed-start", 0);
        var seedCount = GetInt(options, "seed-count", 6);
        var worldWidth = GetInt(options, "world-width", 24);
        var worldHeight = GetInt(options, "world-height", 24);
        var regionWidth = GetInt(options, "region-width", 16);
        var regionHeight = GetInt(options, "region-height", 16);
        var sampledRegionsPerWorld = GetInt(options, "sampled-regions-per-world", 8);
        var localWidth = GetInt(options, "local-width", 48);
        var localHeight = GetInt(options, "local-height", 48);
        var localDepth = GetInt(options, "local-depth", 8);
        var ensureBiomeCoverage = GetBool(options, "ensure-biome-coverage", true);
        var maxAdditionalSeeds = GetInt(options, "max-additional-seeds", 24);

        var report = WorldGenAnalyzer.AnalyzePipelineSamples(
            seedStart: seedStart,
            seedCount: seedCount,
            worldWidth: worldWidth,
            worldHeight: worldHeight,
            regionWidth: regionWidth,
            regionHeight: regionHeight,
            sampledRegionsPerWorld: sampledRegionsPerWorld,
            localWidth: localWidth,
            localHeight: localHeight,
            localDepth: localDepth,
            ensureBiomeCoverage: ensureBiomeCoverage,
            maxAdditionalSeeds: maxAdditionalSeeds);

        var payload = new
        {
            command = "analyze-pipeline",
            input = new
            {
                seedStart,
                seedCount,
                worldWidth,
                worldHeight,
                regionWidth,
                regionHeight,
                sampledRegionsPerWorld,
                localWidth,
                localHeight,
                localDepth,
                ensureBiomeCoverage,
                maxAdditionalSeeds,
            },
            report,
        };

        WriteJson(payload, compact: GetBool(options, "compact"));

        var enforce = GetBool(options, "enforce-budgets");
        if (enforce && !report.Passed)
            return 2;

        return 0;
    }

    private static Dictionary<string, int> CountSurfaceTileDefs(GeneratedEmbarkMap map)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var id = map.GetTile(x, y, 0).TileDefId;
            counts.TryGetValue(id, out var current);
            counts[id] = current + 1;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, int> CountWorldBiomes(GeneratedWorldMap map)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var id = map.GetTile(x, y).MacroBiomeId;
            counts.TryGetValue(id, out var current);
            counts[id] = current + 1;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, int> CountRegionVariants(GeneratedRegionMap map)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var id = map.GetTile(x, y).BiomeVariantId;
            counts.TryGetValue(id, out var current);
            counts[id] = current + 1;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsHelp(string token)
        => token is "-h" or "--help" or "help";

    private static WorldLoreConfig? LoadLoreConfigOrNull(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return null;
        return WorldLoreConfigLoader.LoadFromFile(configPath);
    }

    private static int FailUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected token '{token}'. Options must start with '--'.");

            var key = token[2..];
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Option name cannot be empty.");

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[i + 1];
                i++;
            }
            else
            {
                options[key] = "true";
            }
        }

        return options;
    }

    private static int GetInt(Dictionary<string, string> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out var text))
            return fallback;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        throw new ArgumentException($"Option '--{key}' must be an integer. Received '{text}'.");
    }

    private static string? GetString(Dictionary<string, string> options, string key)
        => options.TryGetValue(key, out var value) ? value : null;

    private static bool GetBool(Dictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var text))
            return false;

        if (bool.TryParse(text, out var result))
            return result;

        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new ArgumentException($"Option '--{key}' must be a boolean flag.");
    }

    private static bool GetBool(Dictionary<string, string> options, string key, bool fallback)
    {
        if (!options.ContainsKey(key))
            return fallback;
        return GetBool(options, key);
    }

    private static void WriteJson<T>(T payload, bool compact)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = !compact,
        });
        Console.WriteLine(json);
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            $"""
            DwarfFortress.WorldGen.Cli

            Commands:
              generate-map
                --seed <int>          (default: 0)
                --width <int>         (default: 48)
                --height <int>        (default: 48)
                --depth <int>         (default: 8)
                --biome <id>          (optional)
                --config <path>       (optional; lore config JSON)
                --compact             (optional)

              generate-world
                --seed <int>          (default: 0)
                --width <int>         (default: 64)
                --height <int>        (default: 64)
                --compact             (optional)

              generate-region
                --seed <int>          (default: 0)
                --world-width <int>   (default: 64)
                --world-height <int>  (default: 64)
                --wx <int>            (default: world-width / 2)
                --wy <int>            (default: world-height / 2)
                --region-width <int>  (default: 32)
                --region-height <int> (default: 32)
                --compact             (optional)

              generate-lore
                --seed <int>          (default: 0)
                --width <int>         (default: 48)
                --height <int>        (default: 48)
                --depth <int>         (default: 8)
                --history <int>       (default: 20, max events emitted)
                --config <path>       (optional; lore config JSON)
                --compact             (optional)

              analyze-depth
                --seed-start <int>    (default: 0)
                --seed-count <int>    (default: 50)
                --width <int>         (default: 48)
                --height <int>        (default: 48)
                --depth <int>         (default: 8)
                --config <path>       (optional; lore config JSON)
                --enforce-budgets     (optional; exits 2 if budgets fail)
                --compact             (optional)

              analyze-pipeline
                --seed-start <int>               (default: 0)
                --seed-count <int>               (default: 6)
                --world-width <int>              (default: 24)
                --world-height <int>             (default: 24)
                --region-width <int>             (default: 16)
                --region-height <int>            (default: 16)
                --sampled-regions-per-world <int> (default: 8)
                --local-width <int>              (default: 48)
                --local-height <int>             (default: 48)
                --local-depth <int>              (default: 8)
                --ensure-biome-coverage <bool>   (default: true)
                --max-additional-seeds <int>     (default: 24)
                --enforce-budgets                (optional; exits 2 if budgets fail)
                --compact                        (optional)

            Examples:
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- generate-map --seed 42 --biome {MacroBiomeIds.ConiferForest}
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- generate-world --seed 42
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- generate-region --seed 42 --wx 8 --wy 12
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- generate-lore --seed 42 --history 12
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- analyze-depth --seed-count 100 --enforce-budgets
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- analyze-pipeline --seed-count 12 --enforce-budgets
            """);
    }
}
