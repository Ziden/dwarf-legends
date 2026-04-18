using System.Globalization;
using System.Text;
using System.Text.Json;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.Story;
using DwarfFortress.WorldGen.World;

return Cli.Run(args);

internal static partial class Cli
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
                "debug-world-tile-ascii" => RunDebugWorldTileAscii(options),
                "debug-embark-window-ascii" => RunDebugEmbarkWindowAscii(options),
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

    private static int RunDebugWorldTileAscii(Dictionary<string, string> options)
    {
        var seed = GetInt(options, "seed", 0);
        var worldWidth = GetInt(options, "world-width", 64);
        var worldHeight = GetInt(options, "world-height", 64);
        var wx = GetInt(options, "wx", worldWidth / 2);
        var wy = GetInt(options, "wy", worldHeight / 2);
        var regionWidth = GetInt(options, "region-width", 8);
        var regionHeight = GetInt(options, "region-height", 8);
        var localWidth = GetInt(options, "local-width", 48);
        var localHeight = GetInt(options, "local-height", 48);
        var localDepth = GetInt(options, "local-depth", 8);
        var z = GetInt(options, "z", 0);
        var sampleStep = Math.Max(1, GetInt(options, "sample-step", 8));
        var maxSeams = Math.Max(0, GetInt(options, "max-seams", 12));

        if (wx < 0 || wx >= worldWidth || wy < 0 || wy >= worldHeight)
            throw new ArgumentOutOfRangeException("--wx/--wy", "Selected world tile is outside generated world bounds.");
        if (z < 0 || z >= localDepth)
            throw new ArgumentOutOfRangeException(nameof(z), $"Requested z layer {z} is outside local depth {localDepth}.");

        var worldGenerator = new WorldLayerGenerator();
        var regionGenerator = new RegionLayerGenerator();
        var localGenerator = new LocalLayerGenerator();

        var world = worldGenerator.Generate(seed, worldWidth, worldHeight);
        var region = regionGenerator.Generate(world, new WorldCoord(wx, wy), regionWidth, regionHeight);
        var parent = world.GetTile(wx, wy);
        var localSettings = new LocalGenerationSettings(localWidth, localHeight, localDepth);
        var locals = new GeneratedEmbarkMap[regionWidth, regionHeight];

        for (var regionY = 0; regionY < regionHeight; regionY++)
        for (var regionX = 0; regionX < regionWidth; regionX++)
        {
            locals[regionX, regionY] = localGenerator.Generate(
                region,
                new RegionCoord(wx, wy, regionX, regionY),
                localSettings);
        }

        var seamSummary = AnalyzeLocalSeams(locals, maxSeams);
        Console.WriteLine(BuildWorldTileAsciiDump(
            seed,
            worldWidth,
            worldHeight,
            wx,
            wy,
            parent,
            region,
            locals,
            z,
            sampleStep,
            seamSummary));

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

    private static string BuildWorldTileAsciiDump(
        int seed,
        int worldWidth,
        int worldHeight,
        int wx,
        int wy,
        GeneratedWorldTile parent,
        GeneratedRegionMap region,
        GeneratedEmbarkMap[,] locals,
        int z,
        int sampleStep,
        LocalSeamDebugSummary seamSummary)
    {
        var sampleXs = BuildSampleIndices(locals[0, 0].Width, sampleStep);
        var sampleYs = BuildSampleIndices(locals[0, 0].Height, sampleStep);
        var cellSampleWidth = sampleXs.Length;
        var separator = BuildHorizontalSeparator(region.Width, cellSampleWidth);
        var builder = new StringBuilder(capacity: 64 * 1024);

        builder.AppendLine("debug-world-tile-ascii");
        builder.AppendLine($"seed={seed} world={worldWidth}x{worldHeight} tile=({wx},{wy}) region={region.Width}x{region.Height} local={locals[0, 0].Width}x{locals[0, 0].Height}x{locals[0, 0].Depth} z={z} sample-step={sampleStep}");
        builder.AppendLine($"parent biome={parent.MacroBiomeId} elevation={parent.ElevationBand:0.000} moisture={parent.MoistureBand:0.000} temperature={parent.TemperatureBand:0.000} forest={parent.ForestCover:0.000} river={(parent.HasRiver ? "yes" : "no")}");
        builder.AppendLine($"region river tiles={CountRegionFlag(region, static tile => tile.HasRiver)} lake tiles={CountRegionFlag(region, static tile => tile.HasLake)} road tiles={CountRegionFlag(region, static tile => tile.HasRoad)} settlements={CountRegionFlag(region, static tile => tile.HasSettlement)}");
        builder.AppendLine($"local seam pairs east={seamSummary.EastPairCount} south={seamSummary.SouthPairCount} boundary samples={seamSummary.SampleCount}");
        builder.AppendLine($"aggregated mismatch ratios surface={seamSummary.SurfaceMismatchRatio:0.000} water={seamSummary.WaterMismatchRatio:0.000} ecology={seamSummary.EcologyMismatchRatio:0.000} tree={seamSummary.TreeMismatchRatio:0.000}");

        if (seamSummary.WorstSeams.Count > 0)
        {
            builder.AppendLine("worst seams:");
            foreach (var seam in seamSummary.WorstSeams)
            {
                var targetX = seam.Axis == 'E' ? seam.RegionX + 1 : seam.RegionX;
                var targetY = seam.Axis == 'S' ? seam.RegionY + 1 : seam.RegionY;
                builder.AppendLine($"  {seam.Axis} ({seam.RegionX},{seam.RegionY})->({targetX},{targetY}) worst={seam.WorstRatio:0.000} surface={seam.Comparison.SurfaceFamilyMismatchRatio:0.000} water={seam.Comparison.WaterMismatchRatio:0.000} ecology={seam.Comparison.EcologyMismatchRatio:0.000} tree={seam.Comparison.TreeMismatchRatio:0.000}");
            }
        }

        builder.AppendLine("legend: ~=water ^=magma T=tree '=plant =road .=sand ,=mud *=snow \"=grass :=soil #=stone X=wall >=stair ?=other");
        builder.AppendLine();

        for (var regionY = 0; regionY < region.Height; regionY++)
        {
            if (regionY > 0)
                builder.AppendLine(separator);

            for (var sampleRow = 0; sampleRow < sampleYs.Length; sampleRow++)
            {
                for (var regionX = 0; regionX < region.Width; regionX++)
                {
                    var local = locals[regionX, regionY];
                    for (var sampleColumn = 0; sampleColumn < sampleXs.Length; sampleColumn++)
                    {
                        var tile = local.GetTile(sampleXs[sampleColumn], sampleYs[sampleRow], z);
                        builder.Append(ResolveAsciiGlyph(tile));
                    }

                    if (regionX < region.Width - 1)
                        builder.Append('|');
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static LocalSeamDebugSummary AnalyzeLocalSeams(GeneratedEmbarkMap[,] locals, int maxSeams)
    {
        var regionWidth = locals.GetLength(0);
        var regionHeight = locals.GetLength(1);
        var eastPairCount = 0;
        var southPairCount = 0;
        var sampleCount = 0;
        var surfaceMismatchCount = 0;
        var waterMismatchCount = 0;
        var ecologyMismatchCount = 0;
        var treeMismatchCount = 0;
        var seamEntries = new List<LocalSeamDebugEntry>((Math.Max(0, regionWidth - 1) * regionHeight) + (regionWidth * Math.Max(0, regionHeight - 1)));

        for (var regionY = 0; regionY < regionHeight; regionY++)
        for (var regionX = 0; regionX < regionWidth - 1; regionX++)
        {
            var comparison = EmbarkBoundaryContinuity.CompareBoundary(locals[regionX, regionY], locals[regionX + 1, regionY], isEastNeighbor: true);
            AccumulateSeamComparison(
                comparison,
                ref sampleCount,
                ref surfaceMismatchCount,
                ref waterMismatchCount,
                ref ecologyMismatchCount,
                ref treeMismatchCount);
            eastPairCount++;
            seamEntries.Add(new LocalSeamDebugEntry('E', regionX, regionY, comparison));
        }

        for (var regionY = 0; regionY < regionHeight - 1; regionY++)
        for (var regionX = 0; regionX < regionWidth; regionX++)
        {
            var comparison = EmbarkBoundaryContinuity.CompareBoundary(locals[regionX, regionY], locals[regionX, regionY + 1], isEastNeighbor: false);
            AccumulateSeamComparison(
                comparison,
                ref sampleCount,
                ref surfaceMismatchCount,
                ref waterMismatchCount,
                ref ecologyMismatchCount,
                ref treeMismatchCount);
            southPairCount++;
            seamEntries.Add(new LocalSeamDebugEntry('S', regionX, regionY, comparison));
        }

        var worstSeams = seamEntries
            .OrderByDescending(entry => entry.WorstRatio)
            .ThenByDescending(entry => entry.Comparison.SurfaceFamilyMismatchRatio)
            .ThenByDescending(entry => entry.Comparison.WaterMismatchRatio)
            .ThenByDescending(entry => entry.Comparison.EcologyMismatchRatio)
            .ThenByDescending(entry => entry.Comparison.TreeMismatchRatio)
            .Take(maxSeams)
            .ToArray();

        return new LocalSeamDebugSummary(
            EastPairCount: eastPairCount,
            SouthPairCount: southPairCount,
            SampleCount: sampleCount,
            SurfaceMismatchCount: surfaceMismatchCount,
            WaterMismatchCount: waterMismatchCount,
            EcologyMismatchCount: ecologyMismatchCount,
            TreeMismatchCount: treeMismatchCount,
            WorstSeams: worstSeams);
    }

    private static void AccumulateSeamComparison(
        EmbarkBoundaryComparison comparison,
        ref int sampleCount,
        ref int surfaceMismatchCount,
        ref int waterMismatchCount,
        ref int ecologyMismatchCount,
        ref int treeMismatchCount)
    {
        sampleCount += comparison.SampleCount;
        surfaceMismatchCount += comparison.SurfaceFamilyMismatchCount;
        waterMismatchCount += comparison.WaterMismatchCount;
        ecologyMismatchCount += comparison.EcologyMismatchCount;
        treeMismatchCount += comparison.TreeMismatchCount;
    }

    private static int[] BuildSampleIndices(int size, int sampleStep)
    {
        var indices = new List<int>((size / sampleStep) + 2);
        for (var index = 0; index < size; index += sampleStep)
            indices.Add(index);

        if (indices.Count == 0 || indices[^1] != size - 1)
            indices.Add(size - 1);

        return indices.ToArray();
    }

    private static string BuildHorizontalSeparator(int regionWidth, int cellSampleWidth)
    {
        var builder = new StringBuilder((regionWidth * cellSampleWidth) + Math.Max(0, regionWidth - 1));
        for (var regionX = 0; regionX < regionWidth; regionX++)
        {
            builder.Append(new string('-', cellSampleWidth));
            if (regionX < regionWidth - 1)
                builder.Append('+');
        }

        return builder.ToString();
    }

    private static int CountRegionFlag(GeneratedRegionMap region, Func<GeneratedRegionTile, bool> selector)
    {
        var count = 0;
        for (var x = 0; x < region.Width; x++)
        for (var y = 0; y < region.Height; y++)
        {
            if (selector(region.GetTile(x, y)))
                count++;
        }

        return count;
    }

    private static char ResolveAsciiGlyph(GeneratedTile tile)
    {
        if (tile.TileDefId == GeneratedTileDefIds.Water || tile.FluidType == GeneratedFluidType.Water)
            return '~';
        if (tile.TileDefId == GeneratedTileDefIds.Magma || tile.FluidType == GeneratedFluidType.Magma)
            return '^';
        if (tile.TileDefId == GeneratedTileDefIds.Tree)
            return 'T';
        if (!string.IsNullOrWhiteSpace(tile.PlantDefId))
            return '\'';
        if (tile.TileDefId == GeneratedTileDefIds.StoneBrick)
            return '=';
        if (tile.TileDefId == GeneratedTileDefIds.Sand)
            return '.';
        if (tile.TileDefId == GeneratedTileDefIds.Mud)
            return ',';
        if (tile.TileDefId == GeneratedTileDefIds.Snow)
            return '*';
        if (tile.TileDefId == GeneratedTileDefIds.Grass)
            return '"';
        if (tile.TileDefId == GeneratedTileDefIds.Soil)
            return ':';
        if (tile.TileDefId == GeneratedTileDefIds.Staircase)
            return '>';
        if (tile.TileDefId == GeneratedTileDefIds.StoneFloor)
            return '#';
        if (tile.TileDefId == GeneratedTileDefIds.StoneWall || tile.TileDefId == GeneratedTileDefIds.SoilWall || !tile.IsPassable)
            return 'X';
        if (tile.TileDefId == GeneratedTileDefIds.Empty)
            return ' ';

        return '?';
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

                            debug-world-tile-ascii
                                --seed <int>          (default: 0)
                                --world-width <int>   (default: 64)
                                --world-height <int>  (default: 64)
                                --wx <int>            (default: world-width / 2)
                                --wy <int>            (default: world-height / 2)
                                --region-width <int>  (default: 8; smaller default for readable ASCII)
                                --region-height <int> (default: 8; smaller default for readable ASCII)
                                --local-width <int>   (default: 48)
                                --local-height <int>  (default: 48)
                                --local-depth <int>   (default: 8)
                                --z <int>             (default: 0)
                                --sample-step <int>   (default: 8)
                                --max-seams <int>     (default: 12)

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
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- debug-world-tile-ascii --seed 42 --wx 8 --wy 12 --region-width 8 --region-height 8 --sample-step 8
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- generate-lore --seed 42 --history 12
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- analyze-depth --seed-count 100 --enforce-budgets
              dotnet run --project src/DwarfFortress.WorldGen.Cli -- analyze-pipeline --seed-count 12 --enforce-budgets
            """);
    }

    private readonly record struct LocalSeamDebugEntry(
        char Axis,
        int RegionX,
        int RegionY,
        EmbarkBoundaryComparison Comparison)
    {
        public float WorstRatio
            => Math.Max(
                Math.Max(Comparison.SurfaceFamilyMismatchRatio, Comparison.WaterMismatchRatio),
                Math.Max(Comparison.EcologyMismatchRatio, Comparison.TreeMismatchRatio));
    }

    private readonly record struct LocalSeamDebugSummary(
        int EastPairCount,
        int SouthPairCount,
        int SampleCount,
        int SurfaceMismatchCount,
        int WaterMismatchCount,
        int EcologyMismatchCount,
        int TreeMismatchCount,
        IReadOnlyList<LocalSeamDebugEntry> WorstSeams)
    {
        public float SurfaceMismatchRatio => Ratio(SurfaceMismatchCount, SampleCount);
        public float WaterMismatchRatio => Ratio(WaterMismatchCount, SampleCount);
        public float EcologyMismatchRatio => Ratio(EcologyMismatchCount, SampleCount);
        public float TreeMismatchRatio => Ratio(TreeMismatchCount, SampleCount);

        private static float Ratio(int numerator, int denominator)
            => denominator <= 0 ? 0f : numerator / (float)denominator;
    }
}


