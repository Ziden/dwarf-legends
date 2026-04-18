using System;
using System.Collections.Generic;
using System.Text;
using GameMapGenerationService = DwarfFortress.GameLogic.World.MapGenerationService;
using GameMapGenerationSettings = DwarfFortress.GameLogic.World.MapGenerationSettings;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Local;
using DwarfFortress.WorldGen.Maps;
using DwarfFortress.WorldGen.Regions;
using DwarfFortress.WorldGen.World;

internal static partial class Cli
{
    private static int RunDebugEmbarkWindowAscii(Dictionary<string, string> options)
    {
        var seed = GetInt(options, "seed", 0);
        var worldWidth = GetInt(options, "world-width", 64);
        var worldHeight = GetInt(options, "world-height", 64);
        var regionWidth = GetInt(options, "region-width", 32);
        var regionHeight = GetInt(options, "region-height", 32);
        var localWidth = GetInt(options, "local-width", 48);
        var localHeight = GetInt(options, "local-height", 48);
        var localDepth = GetInt(options, "local-depth", 8);
        var windowWidth = Math.Max(1, GetInt(options, "window-width", 4));
        var windowHeight = Math.Max(1, GetInt(options, "window-height", 4));
        var z = GetInt(options, "z", 0);
        var sampleStep = Math.Max(1, GetInt(options, "sample-step", 1));
        var maxSeams = Math.Max(0, GetInt(options, "max-seams", 12));

        if (z < 0 || z >= localDepth)
            throw new ArgumentOutOfRangeException(nameof(z), $"Requested z layer {z} is outside local depth {localDepth}.");

        var generationSettings = new GameMapGenerationSettings(
            WorldWidth: worldWidth,
            WorldHeight: worldHeight,
            RegionWidth: regionWidth,
            RegionHeight: regionHeight,
            EnableHistory: true,
            SimulatedHistoryYears: 125);
        var service = new GameMapGenerationService();
        var defaultAnchor = service.ResolveDefaultRegionCoord(seed, generationSettings);
        var anchor = new RegionCoord(
            GetInt(options, "wx", defaultAnchor.WorldX),
            GetInt(options, "wy", defaultAnchor.WorldY),
            GetInt(options, "rx", defaultAnchor.RegionX),
            GetInt(options, "ry", defaultAnchor.RegionY));

        ValidateRegionCoord(anchor, worldWidth, worldHeight, regionWidth, regionHeight);

        var totalRegionWidth = worldWidth * regionWidth;
        var totalRegionHeight = worldHeight * regionHeight;
        var resolvedWindowWidth = Math.Min(windowWidth, totalRegionWidth);
        var resolvedWindowHeight = Math.Min(windowHeight, totalRegionHeight);
        var anchorAbsoluteX = (anchor.WorldX * regionWidth) + anchor.RegionX;
        var anchorAbsoluteY = (anchor.WorldY * regionHeight) + anchor.RegionY;
        var startAbsoluteX = Math.Clamp(anchorAbsoluteX - (resolvedWindowWidth / 2), 0, Math.Max(0, totalRegionWidth - resolvedWindowWidth));
        var startAbsoluteY = Math.Clamp(anchorAbsoluteY - (resolvedWindowHeight / 2), 0, Math.Max(0, totalRegionHeight - resolvedWindowHeight));

        var world = service.GetOrCreateWorld(seed, generationSettings);
        var anchorWorldTile = world.GetTile(anchor.WorldX, anchor.WorldY);
        var anchorRegionMap = service.GetOrCreateRegion(seed, new WorldCoord(anchor.WorldX, anchor.WorldY), generationSettings);
        var anchorRegionTile = anchorRegionMap.GetTile(anchor.RegionX, anchor.RegionY);
        var localSettings = new LocalGenerationSettings(localWidth, localHeight, localDepth);
        var locals = new GeneratedEmbarkMap[resolvedWindowWidth, resolvedWindowHeight];
        var regionCoords = new RegionCoord[resolvedWindowWidth, resolvedWindowHeight];

        for (var windowY = 0; windowY < resolvedWindowHeight; windowY++)
        for (var windowX = 0; windowX < resolvedWindowWidth; windowX++)
        {
            var absoluteRegionX = startAbsoluteX + windowX;
            var absoluteRegionY = startAbsoluteY + windowY;
            var coord = ResolveAbsoluteRegionCoord(absoluteRegionX, absoluteRegionY, regionWidth, regionHeight);
            regionCoords[windowX, windowY] = coord;
            locals[windowX, windowY] = service.GetOrCreateLocal(seed, coord, localSettings, generationSettings);
        }

        var seamSummary = AnalyzeEmbarkWindowLocalSeams(locals, regionCoords, maxSeams);
        Console.WriteLine(BuildEmbarkWindowAsciiDump(
            seed,
            generationSettings,
            defaultAnchor,
            anchor,
            ResolveAbsoluteRegionCoord(startAbsoluteX, startAbsoluteY, regionWidth, regionHeight),
            anchorWorldTile,
            anchorRegionTile,
            locals,
            regionCoords,
            z,
            sampleStep,
            seamSummary));

        return 0;
    }

    private static string BuildEmbarkWindowAsciiDump(
        int seed,
        GameMapGenerationSettings generationSettings,
        RegionCoord defaultAnchor,
        RegionCoord anchor,
        RegionCoord windowStart,
        GeneratedWorldTile anchorWorldTile,
        GeneratedRegionTile anchorRegionTile,
        GeneratedEmbarkMap[,] locals,
        RegionCoord[,] regionCoords,
        int z,
        int sampleStep,
        EmbarkWindowSeamDebugSummary seamSummary)
    {
        var sampleXs = BuildSampleIndices(locals[0, 0].Width, sampleStep);
        var sampleYs = BuildSampleIndices(locals[0, 0].Height, sampleStep);
        var cellSampleWidth = sampleXs.Length;
        var separator = BuildHorizontalSeparator(locals.GetLength(0), cellSampleWidth);
        var builder = new StringBuilder(capacity: 96 * 1024);

        builder.AppendLine("debug-embark-window-ascii");
        builder.AppendLine($"seed={seed} world={generationSettings.WorldWidth}x{generationSettings.WorldHeight} region-grid={generationSettings.RegionWidth}x{generationSettings.RegionHeight} local={locals[0, 0].Width}x{locals[0, 0].Height}x{locals[0, 0].Depth} z={z} sample-step={sampleStep}");
        builder.AppendLine($"default embark={FormatRegionCoord(defaultAnchor)} anchor={FormatRegionCoord(anchor)} window-start={FormatRegionCoord(windowStart)} size={locals.GetLength(0)}x{locals.GetLength(1)}");
        builder.AppendLine($"anchor macro={anchorWorldTile.MacroBiomeId} anchor region biome={anchorRegionTile.BiomeVariantId} vegetation={anchorRegionTile.VegetationDensity:0.000} groundwater={anchorRegionTile.Groundwater:0.000} river={(anchorRegionTile.HasRiver ? "yes" : "no")}");
        builder.AppendLine($"local seam pairs east={seamSummary.EastPairCount} south={seamSummary.SouthPairCount} boundary samples={seamSummary.SampleCount}");
        builder.AppendLine($"aggregated mismatch ratios surface={seamSummary.SurfaceMismatchRatio:0.000} water={seamSummary.WaterMismatchRatio:0.000} ecology={seamSummary.EcologyMismatchRatio:0.000} tree={seamSummary.TreeMismatchRatio:0.000}");
        builder.AppendLine($"local tree tiles min={ResolveMinSurfaceTileCount(locals, GeneratedTileDefIds.Tree)} avg={ResolveAverageSurfaceTileCount(locals, GeneratedTileDefIds.Tree):0.0} max={ResolveMaxSurfaceTileCount(locals, GeneratedTileDefIds.Tree)}");
        builder.AppendLine("tree tile grid:");
        AppendSurfaceTileCountGrid(builder, locals, regionCoords, GeneratedTileDefIds.Tree);

        if (seamSummary.WorstSeams.Count > 0)
        {
            builder.AppendLine("worst seams:");
            foreach (var seam in seamSummary.WorstSeams)
                builder.AppendLine($"  {seam.Axis} idx=({seam.SourceIndexX},{seam.SourceIndexY})->({seam.TargetIndexX},{seam.TargetIndexY}) coords={FormatRegionCoord(seam.SourceCoord)}->{FormatRegionCoord(seam.TargetCoord)} worst={seam.WorstRatio:0.000} surface={seam.Comparison.SurfaceFamilyMismatchRatio:0.000} water={seam.Comparison.WaterMismatchRatio:0.000} ecology={seam.Comparison.EcologyMismatchRatio:0.000} tree={seam.Comparison.TreeMismatchRatio:0.000}");
        }

        builder.AppendLine("legend: ~=water ^=magma T=tree '=plant =road .=sand ,=mud *=snow \"=grass :=soil #=stone X=wall >=stair ?=other");
        builder.AppendLine();

        for (var windowY = 0; windowY < locals.GetLength(1); windowY++)
        {
            if (windowY > 0)
                builder.AppendLine(separator);

            for (var sampleRow = 0; sampleRow < sampleYs.Length; sampleRow++)
            {
                for (var windowX = 0; windowX < locals.GetLength(0); windowX++)
                {
                    var local = locals[windowX, windowY];
                    for (var sampleColumn = 0; sampleColumn < sampleXs.Length; sampleColumn++)
                        builder.Append(ResolveAsciiGlyph(local.GetTile(sampleXs[sampleColumn], sampleYs[sampleRow], z)));

                    if (windowX < locals.GetLength(0) - 1)
                        builder.Append('|');
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static EmbarkWindowSeamDebugSummary AnalyzeEmbarkWindowLocalSeams(GeneratedEmbarkMap[,] locals, RegionCoord[,] regionCoords, int maxSeams)
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
        var seamEntries = new List<EmbarkWindowSeamDebugEntry>((Math.Max(0, regionWidth - 1) * regionHeight) + (regionWidth * Math.Max(0, regionHeight - 1)));

        for (var regionY = 0; regionY < regionHeight; regionY++)
        for (var regionX = 0; regionX < regionWidth - 1; regionX++)
        {
            var comparison = EmbarkBoundaryContinuity.CompareBoundary(locals[regionX, regionY], locals[regionX + 1, regionY], isEastNeighbor: true);
            AccumulateEmbarkWindowSeamComparison(
                comparison,
                ref sampleCount,
                ref surfaceMismatchCount,
                ref waterMismatchCount,
                ref ecologyMismatchCount,
                ref treeMismatchCount);
            eastPairCount++;
            seamEntries.Add(new EmbarkWindowSeamDebugEntry(
                'E',
                regionX,
                regionY,
                regionX + 1,
                regionY,
                regionCoords[regionX, regionY],
                regionCoords[regionX + 1, regionY],
                comparison));
        }

        for (var regionY = 0; regionY < regionHeight - 1; regionY++)
        for (var regionX = 0; regionX < regionWidth; regionX++)
        {
            var comparison = EmbarkBoundaryContinuity.CompareBoundary(locals[regionX, regionY], locals[regionX, regionY + 1], isEastNeighbor: false);
            AccumulateEmbarkWindowSeamComparison(
                comparison,
                ref sampleCount,
                ref surfaceMismatchCount,
                ref waterMismatchCount,
                ref ecologyMismatchCount,
                ref treeMismatchCount);
            southPairCount++;
            seamEntries.Add(new EmbarkWindowSeamDebugEntry(
                'S',
                regionX,
                regionY,
                regionX,
                regionY + 1,
                regionCoords[regionX, regionY],
                regionCoords[regionX, regionY + 1],
                comparison));
        }

        var worstSeams = seamEntries
            .OrderByDescending(entry => entry.WorstRatio)
            .ThenByDescending(entry => entry.Comparison.SurfaceFamilyMismatchRatio)
            .ThenByDescending(entry => entry.Comparison.WaterMismatchRatio)
            .ThenByDescending(entry => entry.Comparison.EcologyMismatchRatio)
            .ThenByDescending(entry => entry.Comparison.TreeMismatchRatio)
            .Take(maxSeams)
            .ToArray();

        return new EmbarkWindowSeamDebugSummary(
            EastPairCount: eastPairCount,
            SouthPairCount: southPairCount,
            SampleCount: sampleCount,
            SurfaceMismatchCount: surfaceMismatchCount,
            WaterMismatchCount: waterMismatchCount,
            EcologyMismatchCount: ecologyMismatchCount,
            TreeMismatchCount: treeMismatchCount,
            WorstSeams: worstSeams);
    }

    private static void AccumulateEmbarkWindowSeamComparison(
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

    private static RegionCoord ResolveAbsoluteRegionCoord(int absoluteRegionX, int absoluteRegionY, int regionWidth, int regionHeight)
        => new(
            absoluteRegionX / regionWidth,
            absoluteRegionY / regionHeight,
            absoluteRegionX % regionWidth,
            absoluteRegionY % regionHeight);

    private static void ValidateRegionCoord(RegionCoord coord, int worldWidth, int worldHeight, int regionWidth, int regionHeight)
    {
        if (coord.WorldX < 0 || coord.WorldX >= worldWidth || coord.WorldY < 0 || coord.WorldY >= worldHeight)
            throw new ArgumentOutOfRangeException(nameof(coord), "Anchor world coordinate is outside generated world bounds.");
        if (coord.RegionX < 0 || coord.RegionX >= regionWidth || coord.RegionY < 0 || coord.RegionY >= regionHeight)
            throw new ArgumentOutOfRangeException(nameof(coord), "Anchor region coordinate is outside generated region bounds.");
    }

    private static string FormatRegionCoord(RegionCoord coord)
        => $"({coord.WorldX},{coord.WorldY})/({coord.RegionX},{coord.RegionY})";

    private static void AppendSurfaceTileCountGrid(StringBuilder builder, GeneratedEmbarkMap[,] locals, RegionCoord[,] regionCoords, string tileDefId)
    {
        for (var windowY = 0; windowY < locals.GetLength(1); windowY++)
        {
            for (var windowX = 0; windowX < locals.GetLength(0); windowX++)
            {
                if (windowX > 0)
                    builder.Append("  ");

                builder.Append($"{FormatRegionCoord(regionCoords[windowX, windowY])}={CountSurfaceTiles(locals[windowX, windowY], tileDefId),4}");
            }

            builder.AppendLine();
        }
    }

    private static int ResolveMinSurfaceTileCount(GeneratedEmbarkMap[,] locals, string tileDefId)
    {
        var min = int.MaxValue;
        for (var y = 0; y < locals.GetLength(1); y++)
        for (var x = 0; x < locals.GetLength(0); x++)
            min = Math.Min(min, CountSurfaceTiles(locals[x, y], tileDefId));

        return min == int.MaxValue ? 0 : min;
    }

    private static int ResolveMaxSurfaceTileCount(GeneratedEmbarkMap[,] locals, string tileDefId)
    {
        var max = 0;
        for (var y = 0; y < locals.GetLength(1); y++)
        for (var x = 0; x < locals.GetLength(0); x++)
            max = Math.Max(max, CountSurfaceTiles(locals[x, y], tileDefId));

        return max;
    }

    private static float ResolveAverageSurfaceTileCount(GeneratedEmbarkMap[,] locals, string tileDefId)
    {
        var total = 0;
        var count = 0;
        for (var y = 0; y < locals.GetLength(1); y++)
        for (var x = 0; x < locals.GetLength(0); x++)
        {
            total += CountSurfaceTiles(locals[x, y], tileDefId);
            count++;
        }

        return count == 0 ? 0f : total / (float)count;
    }

    private static int CountSurfaceTiles(GeneratedEmbarkMap map, string tileDefId)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (string.Equals(map.GetTile(x, y, 0).TileDefId, tileDefId, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private readonly record struct EmbarkWindowSeamDebugEntry(
        char Axis,
        int SourceIndexX,
        int SourceIndexY,
        int TargetIndexX,
        int TargetIndexY,
        RegionCoord SourceCoord,
        RegionCoord TargetCoord,
        EmbarkBoundaryComparison Comparison)
    {
        public float WorstRatio
            => Math.Max(
                Math.Max(Comparison.SurfaceFamilyMismatchRatio, Comparison.WaterMismatchRatio),
                Math.Max(Comparison.EcologyMismatchRatio, Comparison.TreeMismatchRatio));
    }

    private readonly record struct EmbarkWindowSeamDebugSummary(
        int EastPairCount,
        int SouthPairCount,
        int SampleCount,
        int SurfaceMismatchCount,
        int WaterMismatchCount,
        int EcologyMismatchCount,
        int TreeMismatchCount,
        IReadOnlyList<EmbarkWindowSeamDebugEntry> WorstSeams)
    {
        public float SurfaceMismatchRatio => Ratio(SurfaceMismatchCount, SampleCount);
        public float WaterMismatchRatio => Ratio(WaterMismatchCount, SampleCount);
        public float EcologyMismatchRatio => Ratio(EcologyMismatchCount, SampleCount);
        public float TreeMismatchRatio => Ratio(TreeMismatchCount, SampleCount);

        private static float Ratio(int numerator, int denominator)
            => denominator <= 0 ? 0f : numerator / (float)denominator;
    }
}
