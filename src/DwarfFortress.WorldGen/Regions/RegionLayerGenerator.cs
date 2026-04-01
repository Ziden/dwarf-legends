using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.History;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.World;

namespace DwarfFortress.WorldGen.Regions;

public interface IRegionLayerGenerator
{
    GeneratedRegionMap Generate(
        GeneratedWorldMap world,
        WorldCoord worldCoord,
        int regionWidth = 32,
        int regionHeight = 32,
        GeneratedWorldHistory? history = null);
}

public sealed class RegionLayerGenerator : IRegionLayerGenerator
{
    private enum BoundaryEdge : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    private readonly record struct BoundaryPortal(BoundaryEdge Edge, int X, int Y, float SharedDischarge);

    private static readonly (int Dx, int Dy)[] CardinalNeighborOffsets =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    private static readonly (int Dx, int Dy)[] SurroundingNeighborOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0),           (1, 0),
        (-1, 1),  (0, 1),  (1, 1),
    ];

    private static readonly (int Dx, int Dy)[] CenterAndCardinalOffsets =
    [
        (0, 0),
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    public GeneratedRegionMap Generate(
        GeneratedWorldMap world,
        WorldCoord worldCoord,
        int regionWidth = 32,
        int regionHeight = 32,
        GeneratedWorldHistory? history = null)
    {
        if (regionWidth <= 0) throw new ArgumentOutOfRangeException(nameof(regionWidth));
        if (regionHeight <= 0) throw new ArgumentOutOfRangeException(nameof(regionHeight));
        if (worldCoord.X < 0 || worldCoord.X >= world.Width || worldCoord.Y < 0 || worldCoord.Y >= world.Height)
            throw new ArgumentOutOfRangeException(nameof(worldCoord), "World coordinate is outside world bounds.");

        var parent = world.GetTile(worldCoord.X, worldCoord.Y);
        var regionSeed = SeedHash.Hash(world.Seed, worldCoord.X, worldCoord.Y, 31001);
        var terrainSeed = SeedHash.Hash(world.Seed, 91007, 0, 0);
        var map = new GeneratedRegionMap(
            regionSeed,
            regionWidth,
            regionHeight,
            worldCoord,
            parentMacroBiomeId: parent.MacroBiomeId,
            parentForestCover: parent.ForestCover,
            parentMountainCover: parent.MountainCover,
            parentRelief: parent.Relief,
            parentMoistureBand: parent.MoistureBand,
            parentTemperatureBand: parent.TemperatureBand,
            parentHasRiver: parent.HasRiver,
            parentRiverOrder: parent.RiverOrder,
            parentRiverDischarge: parent.RiverDischarge);

        var cellCount = regionWidth * regionHeight;
        var roadGenerationEnabled = WorldGenFeatureFlags.EnableRoadGeneration;
        var elevation = new float[cellCount];
        var moisture = new float[cellCount];
        var ruggedness = new float[cellCount];
        var macroElevation = new float[cellCount];
        var macroTemperature = new float[cellCount];
        var macroMoisture = new float[cellCount];
        var macroDrainage = new float[cellCount];
        var macroRiverInfluence = new float[cellCount];
        var macroForestCover = new float[cellCount];
        var macroMountainCover = new float[cellCount];
        var macroRelief = new float[cellCount];

        for (var y = 0; y < regionHeight; y++)
        for (var x = 0; x < regionWidth; x++)
        {
            var idx = IndexOf(x, y, regionWidth);
            var fx = regionWidth <= 1 ? 0f : x / (float)(regionWidth - 1);
            var fy = regionHeight <= 1 ? 0f : y / (float)(regionHeight - 1);

            // Blend two sampling spaces:
            // 1) edge-projected world sampling for shared-border continuity
            // 2) interior-focused sampling so the region center remains faithful to its parent world tile
            var edgeWorldX = worldCoord.X - 0.5f + fx;
            var edgeWorldY = worldCoord.Y - 0.5f + fy;
            var interiorWorldX = worldCoord.X + ((fx - 0.5f) * 0.72f);
            var interiorWorldY = worldCoord.Y + ((fy - 0.5f) * 0.72f);
            var interiorBlend = ResolveInteriorBlendWeight(fx, fy);

            var sampledElevation = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.ElevationBand);
            var sampledTemperature = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.TemperatureBand);
            var sampledMoisture = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.MoistureBand);
            var sampledDrainage = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.DrainageBand);
            var sampledRiverInfluence = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.HasRiver ? 1f : 0f);
            var sampledForestCover = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.ForestCover);
            var sampledMountainCover = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.MountainCover);
            var sampledRelief = SampleWorldScalarBlended(
                world,
                edgeWorldX,
                edgeWorldY,
                interiorWorldX,
                interiorWorldY,
                interiorBlend,
                static tile => tile.Relief);

            macroElevation[idx] = sampledElevation;
            macroTemperature[idx] = sampledTemperature;
            macroMoisture[idx] = sampledMoisture;
            macroDrainage[idx] = sampledDrainage;
            macroRiverInfluence[idx] = sampledRiverInfluence;
            macroForestCover[idx] = sampledForestCover;
            macroMountainCover[idx] = sampledMountainCover;
            macroRelief[idx] = sampledRelief;

            var macroTerrain = CoherentNoise.DomainWarpedFractal2D(
                terrainSeed, interiorWorldX * 1.7f, interiorWorldY * 1.7f, octaves: 4, lacunarity: 2f, gain: 0.52f, warpStrength: 0.32f, salt: 101);
            var ridge = CoherentNoise.Ridged2D(
                terrainSeed, interiorWorldX * 3.9f, interiorWorldY * 3.9f, octaves: 4, lacunarity: 2.05f, gain: 0.54f, salt: 211);
            var micro = CoherentNoise.Fractal2D(
                terrainSeed, interiorWorldX * 8.1f, interiorWorldY * 8.1f, octaves: 3, lacunarity: 2f, gain: 0.5f, salt: 307);

            elevation[idx] = Math.Clamp(
                (sampledElevation * 0.56f) +
                (macroTerrain * 0.26f) +
                (ridge * 0.14f) +
                ((micro - 0.5f) * 0.12f) -
                (sampledRiverInfluence * 0.05f), 0f, 1f);

            moisture[idx] = Math.Clamp(
                (sampledMoisture * 0.50f) +
                (CoherentNoise.DomainWarpedFractal2D(
                    terrainSeed, interiorWorldX * 2.3f, interiorWorldY * 2.3f, octaves: 3, lacunarity: 2f, gain: 0.5f, warpStrength: 0.22f, salt: 401) * 0.35f) +
                ((1f - elevation[idx]) * 0.10f) +
                (sampledDrainage * 0.08f) +
                (sampledRiverInfluence * 0.07f), 0f, 1f);

            ruggedness[idx] = Math.Clamp(
                (ridge * 0.58f) +
                (CoherentNoise.Fractal2D(terrainSeed, interiorWorldX * 5.3f, interiorWorldY * 5.3f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 509) * 0.30f) +
                (MathF.Abs(sampledElevation - 0.5f) * 0.12f), 0f, 1f);
        }

        var downstream = BuildFlowDirections(elevation, regionWidth, regionHeight, regionSeed);
        var accumulation = BuildFlowAccumulation(elevation, moisture, downstream);
        var maxAccumulation = Math.Max(1f, accumulation.Max());

        var boundaryPortals = BuildBoundaryRiverPortals(world, worldCoord, regionWidth, regionHeight, parent);
        var forcedRiverMask = BuildForcedRiverMask(boundaryPortals, elevation, regionWidth, regionHeight, regionSeed);
        var boundaryDischargeContracts = BuildBoundaryDischargeContracts(boundaryPortals, regionWidth, regionHeight);
        var roadBoundaryContracts = roadGenerationEnabled
            ? BuildBoundaryRoadContracts(world, worldCoord, regionWidth, regionHeight, parent)
            : new RegionRoadEdges[cellCount];
        var forcedRoadMask = roadGenerationEnabled
            ? BuildForcedRoadMask(roadBoundaryContracts, elevation, ruggedness, regionWidth, regionHeight, regionSeed)
            : new bool[cellCount];
        var parentDischargeScale = parent.HasRiver
            ? Math.Clamp(parent.RiverDischarge / 2.4f, 0.85f, 2.8f)
            : 1f;

        var hasMainRiver = parent.HasRiver || parent.DrainageBand >= 0.32f || boundaryPortals.Count > 0;
        var isOceanParent = MacroBiomeIds.IsOcean(parent.MacroBiomeId);
        var riverThreshold = 4f + ((1f - parent.DrainageBand) * (cellCount * 0.08f));
        if (parent.HasRiver)
            riverThreshold *= 0.78f;
        var lakeThreshold = riverThreshold * 0.45f;

        for (var y = 0; y < regionHeight; y++)
        for (var x = 0; x < regionWidth; x++)
        {
            var idx = IndexOf(x, y, regionWidth);
            var fx = regionWidth <= 1 ? 0f : x / (float)(regionWidth - 1);
            var fy = regionHeight <= 1 ? 0f : y / (float)(regionHeight - 1);

            var slopeNorm = EstimateSlopeNorm(x, y, elevation, ruggedness, regionWidth, regionHeight);
            var slope = (byte)Math.Clamp((int)MathF.Round(slopeNorm * 255f), 0, 255);
            var temperatureBand = ResolveRegionTemperatureBand(
                macroTemperature[idx],
                elevation[idx],
                slopeNorm,
                parent.MacroBiomeId);

            var valleyHydrology = Math.Clamp(accumulation[idx] / (riverThreshold * 1.25f), 0f, 1f);
            var valleyBoost = valleyHydrology * 0.22f;

            var resources = Math.Clamp(
                (macroElevation[idx] * 0.28f) +
                (macroMountainCover[idx] * 0.22f) +
                (macroRelief[idx] * 0.16f) +
                (ruggedness[idx] * 0.32f) +
                (slopeNorm * 0.24f) +
                ((1f - moisture[idx]) * 0.16f), 0f, 1f);

            var naturalRiver = hasMainRiver &&
                               accumulation[idx] >= riverThreshold &&
                               elevation[idx] <= 0.92f &&
                               downstream[idx] >= 0;
            var hasRiver = naturalRiver || forcedRiverMask[idx];
            var riverDischarge = 0f;
            if (hasRiver)
            {
                var normalizedLocalFlow = accumulation[idx] / Math.Max(1f, riverThreshold);
                riverDischarge = Math.Clamp(normalizedLocalFlow * parentDischargeScale, 1f, 12f);
                if (forcedRiverMask[idx])
                    riverDischarge = Math.Max(riverDischarge, Math.Clamp(parent.RiverDischarge, 1f, 12f));

                var contractDischarge = boundaryDischargeContracts[idx];
                if (contractDischarge > 0f)
                    riverDischarge = contractDischarge;
            }

            var hasLake = !hasRiver &&
                          downstream[idx] < 0 &&
                          x > 0 &&
                          y > 0 &&
                          x < regionWidth - 1 &&
                          y < regionHeight - 1 &&
                          accumulation[idx] >= lakeThreshold &&
                          moisture[idx] >= 0.52f;

            var waterFeatureBoost = (hasRiver ? 0.34f : 0f) + (hasLake ? 0.42f : 0f);
            var soilDepth = Math.Clamp(
                (moisture[idx] * 0.40f) +
                ((1f - slopeNorm) * 0.30f) +
                (valleyHydrology * 0.18f) +
                ((1f - elevation[idx]) * 0.12f) +
                (waterFeatureBoost * 0.12f), 0f, 1f);

            var groundwater = Math.Clamp(
                (moisture[idx] * 0.46f) +
                (macroDrainage[idx] * 0.18f) +
                (valleyHydrology * 0.18f) +
                ((1f - slopeNorm) * 0.08f) +
                (waterFeatureBoost * 0.26f) -
                (Math.Max(0f, elevation[idx] - 0.64f) * 0.20f), 0f, 1f);

            var temperatureSuitability = ResolveTemperatureVegetationSupport(
                macroTemperature[idx],
                macroElevation[idx],
                parent.MacroBiomeId);
            var moistureSuitability = Math.Clamp(
                (moisture[idx] * 0.42f) +
                (macroMoisture[idx] * 0.22f) +
                (macroDrainage[idx] * 0.16f) +
                (valleyHydrology * 0.20f) +
                (waterFeatureBoost * 0.12f), 0f, 1f);
            var soilSuitability = Math.Clamp(
                (soilDepth * 0.46f) +
                (groundwater * 0.38f) +
                ((1f - slopeNorm) * 0.16f), 0f, 1f);
            var vegetationSuitability = Math.Clamp(
                (moistureSuitability * 0.44f) +
                (soilSuitability * 0.34f) +
                (temperatureSuitability * 0.22f), 0f, 1f);

            var macroForestSignal = Math.Clamp(
                (macroForestCover[idx] * 0.66f) +
                (macroMoisture[idx] * 0.16f) +
                (macroRiverInfluence[idx] * 0.10f) +
                (vegetationSuitability * 0.08f), 0f, 1f);
            var localVegetationSupport = Math.Clamp(
                (moisture[idx] * 0.26f) +
                valleyBoost +
                (macroRiverInfluence[idx] * 0.05f) +
                ((1f - slopeNorm) * 0.05f) +
                (vegetationSuitability * 0.26f), 0f, 1f);
            var vegetation = Math.Clamp(
                (macroForestSignal * 0.48f) +
                (localVegetationSupport * 0.32f) +
                (vegetationSuitability * 0.20f) -
                (macroMountainCover[idx] * 0.15f) -
                (slopeNorm * 0.18f), 0f, 1f);

            var settlementNoise = CoherentNoise.Fractal2D(
                regionSeed, fx * 6.1f, fy * 6.1f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 701);
            var hasSettlement = !hasRiver &&
                                !hasLake &&
                                vegetation >= 0.24f &&
                                vegetation <= 0.84f &&
                                slopeNorm <= 0.56f &&
                                settlementNoise >= 0.81f;

            var hasRoad = false;
            if (roadGenerationEnabled)
            {
                var roadNoise = CoherentNoise.Fractal2D(
                    regionSeed, fx * 4.3f, fy * 4.3f, octaves: 2, lacunarity: 2f, gain: 0.5f, salt: 809);
                var forcedRoad = roadBoundaryContracts[idx] != RegionRoadEdges.None || forcedRoadMask[idx];
                hasRoad = !hasRiver &&
                          !hasLake &&
                          (forcedRoad || (slopeNorm <= 0.45f && (roadNoise >= 0.76f || (hasSettlement && roadNoise >= 0.52f))));
            }

            if (isOceanParent)
            {
                hasRiver = false;
                hasLake = true;
                hasSettlement = false;
                hasRoad = false;

                vegetation = parent.MacroBiomeId == MacroBiomeIds.OceanShallow
                    ? Math.Clamp((moisture[idx] * 0.18f) + ((1f - slopeNorm) * 0.12f), 0f, 0.34f)
                    : Math.Clamp((moisture[idx] * 0.08f), 0f, 0.16f);

                resources = Math.Clamp((ruggedness[idx] * 0.20f) + (parent.ElevationBand * 0.10f), 0f, 0.35f);
                slope = (byte)Math.Clamp((int)MathF.Round(slopeNorm * 96f), 0, 96);
                soilDepth = parent.MacroBiomeId == MacroBiomeIds.OceanShallow
                    ? Math.Clamp((1f - elevation[idx]) * 0.22f, 0f, 0.30f)
                    : Math.Clamp((1f - elevation[idx]) * 0.10f, 0f, 0.12f);
                groundwater = parent.MacroBiomeId == MacroBiomeIds.OceanShallow ? 0.92f : 0.98f;
                vegetationSuitability = parent.MacroBiomeId == MacroBiomeIds.OceanShallow
                    ? Math.Clamp((moisture[idx] * 0.18f) + ((1f - slopeNorm) * 0.12f), 0f, 0.28f)
                    : Math.Clamp(moisture[idx] * 0.08f, 0f, 0.14f);
                riverDischarge = 0f;
            }

            var moistureBand = Math.Clamp(
                (moisture[idx] * 0.56f) +
                (macroMoisture[idx] * 0.24f) +
                (groundwater * 0.20f) +
                (hasRiver ? 0.10f : 0f) +
                (hasLake ? 0.18f : 0f),
                0f,
                1f);
            var flowAccumulationBand = Math.Clamp(accumulation[idx] / maxAccumulation, 0f, 1f);
            if (hasRiver)
                flowAccumulationBand = Math.Max(flowAccumulationBand, 0.18f);
            if (hasLake)
                flowAccumulationBand = Math.Max(flowAccumulationBand, 0.12f);
            var biomeVariantId = ResolveBiomeVariantId(parent.MacroBiomeId, vegetation, slopeNorm, hasRiver, hasLake);
            var surfaceClassId = RegionSurfaceResolver.ResolveSurfaceClassId(
                parent.MacroBiomeId,
                biomeVariantId,
                slopeNorm,
                hasRiver,
                hasLake,
                moistureBand,
                groundwater,
                temperatureBand,
                soilDepth);

            map.SetTile(x, y, new GeneratedRegionTile(
                BiomeVariantId: biomeVariantId,
                SurfaceClassId: surfaceClassId,
                Slope: slope,
                HasRiver: hasRiver,
                HasLake: hasLake,
                VegetationDensity: vegetation,
                ResourceRichness: resources,
                SoilDepth: soilDepth,
                Groundwater: groundwater,
                HasRoad: hasRoad,
                HasSettlement: hasSettlement,
                GeologyProfileId: parent.GeologyProfileId,
                RiverDischarge: riverDischarge,
                VegetationSuitability: vegetationSuitability,
                TemperatureBand: temperatureBand,
                MoistureBand: moistureBand,
                FlowAccumulationBand: flowAccumulationBand));
        }

        ApplyRiverEdgeContracts(map, boundaryPortals);
        PruneEdgeLessRiverTiles(map);
        PruneDisconnectedRiverFragments(map, boundaryPortals);
        ApplyRiverEdgeContracts(map, boundaryPortals);
        PruneEdgeLessRiverTiles(map);
        ApplyRiverOrder(map, downstream);
        ApplyRiverValleyShaping(map);
        ApplyVegetationContinuity(map, macroForestCover, macroMoisture, macroMountainCover);
        ReclassifyBiomeVariants(map);
        ApplyHistoryOverlay(map, worldCoord, history, regionSeed, world.Seed, roadBoundaryContracts);
        if (roadGenerationEnabled)
        {
            PromoteRoadBoundaryContractTiles(map, roadBoundaryContracts);
            ApplyRoadEdgeContracts(map, roadBoundaryContracts);
        }
        else
        {
            StripRoadData(map);
        }
        return map;
    }

    private static List<BoundaryPortal> BuildBoundaryRiverPortals(
        GeneratedWorldMap world,
        WorldCoord worldCoord,
        int regionWidth,
        int regionHeight,
        GeneratedWorldTile parentTile)
    {
        var portals = new List<BoundaryPortal>(4);

        if (WorldRiverEdgeMask.Has(parentTile.RiverEdges, WorldRiverEdges.North) &&
            TryGetNeighbor(world, worldCoord.X, worldCoord.Y - 1, out _))
        {
            var x = ResolveSharedBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.North, regionWidth);
            portals.Add(new BoundaryPortal(
                BoundaryEdge.North,
                x,
                0,
                ResolveSharedBoundaryDischarge(world, worldCoord, parentTile, BoundaryEdge.North)));
        }

        if (WorldRiverEdgeMask.Has(parentTile.RiverEdges, WorldRiverEdges.East) &&
            TryGetNeighbor(world, worldCoord.X + 1, worldCoord.Y, out _))
        {
            var y = ResolveSharedBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.East, regionHeight);
            portals.Add(new BoundaryPortal(
                BoundaryEdge.East,
                regionWidth - 1,
                y,
                ResolveSharedBoundaryDischarge(world, worldCoord, parentTile, BoundaryEdge.East)));
        }

        if (WorldRiverEdgeMask.Has(parentTile.RiverEdges, WorldRiverEdges.South) &&
            TryGetNeighbor(world, worldCoord.X, worldCoord.Y + 1, out _))
        {
            var x = ResolveSharedBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.South, regionWidth);
            portals.Add(new BoundaryPortal(
                BoundaryEdge.South,
                x,
                regionHeight - 1,
                ResolveSharedBoundaryDischarge(world, worldCoord, parentTile, BoundaryEdge.South)));
        }

        if (WorldRiverEdgeMask.Has(parentTile.RiverEdges, WorldRiverEdges.West) &&
            TryGetNeighbor(world, worldCoord.X - 1, worldCoord.Y, out _))
        {
            var y = ResolveSharedBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.West, regionHeight);
            portals.Add(new BoundaryPortal(
                BoundaryEdge.West,
                0,
                y,
                ResolveSharedBoundaryDischarge(world, worldCoord, parentTile, BoundaryEdge.West)));
        }

        return portals;
    }

    private static bool TryGetNeighbor(GeneratedWorldMap world, int x, int y, out GeneratedWorldTile neighbor)
    {
        if (x < 0 || x >= world.Width || y < 0 || y >= world.Height)
        {
            neighbor = default;
            return false;
        }

        neighbor = world.GetTile(x, y);
        return true;
    }

    private static float[] BuildBoundaryDischargeContracts(List<BoundaryPortal> boundaryPortals, int width, int height)
    {
        var contracts = new float[width * height];
        foreach (var portal in boundaryPortals)
        {
            var idx = IndexOf(portal.X, portal.Y, width);
            if (portal.SharedDischarge > contracts[idx])
                contracts[idx] = portal.SharedDischarge;
        }

        return contracts;
    }

    private static RegionRoadEdges[] BuildBoundaryRoadContracts(
        GeneratedWorldMap world,
        WorldCoord worldCoord,
        int regionWidth,
        int regionHeight,
        GeneratedWorldTile parentTile)
    {
        var contracts = new RegionRoadEdges[regionWidth * regionHeight];

        if (WorldRoadEdgeMask.Has(parentTile.RoadEdges, WorldRoadEdges.North) &&
            TryGetNeighbor(world, worldCoord.X, worldCoord.Y - 1, out _))
        {
            var x = ResolveSharedRoadBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.North, regionWidth);
            contracts[IndexOf(x, 0, regionWidth)] |= RegionRoadEdges.North;
        }

        if (WorldRoadEdgeMask.Has(parentTile.RoadEdges, WorldRoadEdges.East) &&
            TryGetNeighbor(world, worldCoord.X + 1, worldCoord.Y, out _))
        {
            var y = ResolveSharedRoadBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.East, regionHeight);
            contracts[IndexOf(regionWidth - 1, y, regionWidth)] |= RegionRoadEdges.East;
        }

        if (WorldRoadEdgeMask.Has(parentTile.RoadEdges, WorldRoadEdges.South) &&
            TryGetNeighbor(world, worldCoord.X, worldCoord.Y + 1, out _))
        {
            var x = ResolveSharedRoadBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.South, regionWidth);
            contracts[IndexOf(x, regionHeight - 1, regionWidth)] |= RegionRoadEdges.South;
        }

        if (WorldRoadEdgeMask.Has(parentTile.RoadEdges, WorldRoadEdges.West) &&
            TryGetNeighbor(world, worldCoord.X - 1, worldCoord.Y, out _))
        {
            var y = ResolveSharedRoadBoundaryOffset(world.Seed, worldCoord, BoundaryEdge.West, regionHeight);
            contracts[IndexOf(0, y, regionWidth)] |= RegionRoadEdges.West;
        }

        return contracts;
    }

    private static void ApplyRiverEdgeContracts(GeneratedRegionMap map, List<BoundaryPortal> boundaryPortals)
    {
        if (map.Width <= 0 || map.Height <= 0)
            return;

        var boundaryEdges = new RegionRiverEdges[map.Width * map.Height];
        foreach (var portal in boundaryPortals)
        {
            var idx = IndexOf(portal.X, portal.Y, map.Width);
            boundaryEdges[idx] |= ToRegionEdge(portal.Edge);
        }

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRiver)
                continue;

            var idx = IndexOf(x, y, map.Width);
            var edges = boundaryEdges[idx];

            if (y > 0 && map.GetTile(x, y - 1).HasRiver)
                edges |= RegionRiverEdges.North;
            if (x < map.Width - 1 && map.GetTile(x + 1, y).HasRiver)
                edges |= RegionRiverEdges.East;
            if (y < map.Height - 1 && map.GetTile(x, y + 1).HasRiver)
                edges |= RegionRiverEdges.South;
            if (x > 0 && map.GetTile(x - 1, y).HasRiver)
                edges |= RegionRiverEdges.West;

            map.SetTile(x, y, tile with { RiverEdges = edges });
        }
    }

    private static void StripRoadData(GeneratedRegionMap map)
    {
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRoad && tile.RoadEdges == RegionRoadEdges.None)
                continue;

            map.SetTile(x, y, tile with
            {
                HasRoad = false,
                RoadEdges = RegionRoadEdges.None,
            });
        }
    }

    private static void PromoteRoadBoundaryContractTiles(GeneratedRegionMap map, RegionRoadEdges[] roadBoundaryContracts)
    {
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            if (roadBoundaryContracts[idx] == RegionRoadEdges.None)
                continue;

            var tile = map.GetTile(x, y);
            if (tile.HasRoad || tile.HasRiver || tile.HasLake)
                continue;

            map.SetTile(x, y, tile with { HasRoad = true });
        }
    }

    private static void PruneEdgeLessRiverTiles(GeneratedRegionMap map)
    {
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRiver || tile.RiverEdges != RegionRiverEdges.None)
                continue;

            map.SetTile(x, y, tile with
            {
                HasRiver = false,
                RiverDischarge = 0f,
                RiverOrder = 0,
            });
        }
    }

    private static void ApplyRoadEdgeContracts(GeneratedRegionMap map, RegionRoadEdges[] boundaryContracts)
    {
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            var tile = map.GetTile(x, y);
            if (!tile.HasRoad)
            {
                if (tile.RoadEdges != RegionRoadEdges.None)
                    map.SetTile(x, y, tile with { RoadEdges = RegionRoadEdges.None });
                continue;
            }

            var edges = boundaryContracts[idx];

            if (y > 0 && map.GetTile(x, y - 1).HasRoad)
                edges |= RegionRoadEdges.North;
            if (x < map.Width - 1 && map.GetTile(x + 1, y).HasRoad)
                edges |= RegionRoadEdges.East;
            if (y < map.Height - 1 && map.GetTile(x, y + 1).HasRoad)
                edges |= RegionRoadEdges.South;
            if (x > 0 && map.GetTile(x - 1, y).HasRoad)
                edges |= RegionRoadEdges.West;

            map.SetTile(x, y, tile with { RoadEdges = edges });
        }
    }

    private static void PruneDisconnectedRiverFragments(GeneratedRegionMap map, List<BoundaryPortal> boundaryPortals)
    {
        if (map.Width <= 0 || map.Height <= 0)
            return;

        var anchors = new bool[map.Width * map.Height];
        foreach (var portal in boundaryPortals)
            anchors[IndexOf(portal.X, portal.Y, map.Width)] = true;

        var visited = new bool[anchors.Length];
        var queue = new Queue<int>(32);
        var component = new List<int>(32);

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var start = IndexOf(x, y, map.Width);
            if (visited[start] || !map.GetTile(x, y).HasRiver)
                continue;

            queue.Clear();
            component.Clear();
            queue.Enqueue(start);
            visited[start] = true;

            var hasAnchor = false;
            var touchesBoundary = false;
            var maxDischarge = 0f;

            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();
                component.Add(idx);

                var cx = idx % map.Width;
                var cy = idx / map.Width;
                var tile = map.GetTile(cx, cy);
                if (tile.RiverDischarge > maxDischarge)
                    maxDischarge = tile.RiverDischarge;
                if (anchors[idx])
                    hasAnchor = true;
                if (cx == 0 || cy == 0 || cx == map.Width - 1 || cy == map.Height - 1)
                    touchesBoundary = true;

                TryEnqueueRiverNeighbor(map, visited, queue, cx + 1, cy);
                TryEnqueueRiverNeighbor(map, visited, queue, cx - 1, cy);
                TryEnqueueRiverNeighbor(map, visited, queue, cx, cy + 1);
                TryEnqueueRiverNeighbor(map, visited, queue, cx, cy - 1);
            }

            var keep = hasAnchor || touchesBoundary || component.Count >= 4 || maxDischarge >= 3.5f;
            if (keep)
                continue;

            for (var i = 0; i < component.Count; i++)
            {
                var idx = component[i];
                var cx = idx % map.Width;
                var cy = idx / map.Width;
                var tile = map.GetTile(cx, cy);
                map.SetTile(cx, cy, tile with
                {
                    HasRiver = false,
                    RiverDischarge = 0f,
                    RiverEdges = RegionRiverEdges.None,
                });
            }
        }
    }

    private static void TryEnqueueRiverNeighbor(
        GeneratedRegionMap map,
        bool[] visited,
        Queue<int> queue,
        int x,
        int y)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            return;

        var idx = IndexOf(x, y, map.Width);
        if (visited[idx] || !map.GetTile(x, y).HasRiver)
            return;

        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private static void ApplyRiverOrder(GeneratedRegionMap map, int[] downstream)
    {
        var hasRiver = new bool[map.Width * map.Height];
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            hasRiver[idx] = map.GetTile(x, y).HasRiver;
        }

        var order = BuildRiverOrder(hasRiver, downstream);
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            var tile = map.GetTile(x, y);
            var riverOrder = tile.HasRiver ? order[idx] : (byte)0;
            map.SetTile(x, y, tile with { RiverOrder = riverOrder });
        }
    }

    private static byte[] BuildRiverOrder(bool[] hasRiver, int[] downstream)
    {
        var order = new byte[hasRiver.Length];
        var indegree = new int[hasRiver.Length];
        var upstream = BuildUpstreamIndices(downstream);
        var maxUpstreamOrder = new byte[hasRiver.Length];
        var maxUpstreamOrderCount = new byte[hasRiver.Length];
        var queue = new Queue<int>(hasRiver.Length);

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx])
                continue;

            var next = downstream[idx];
            if (next >= 0 && next < hasRiver.Length && hasRiver[next] && next != idx)
                indegree[next]++;
        }

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (hasRiver[idx] && indegree[idx] == 0)
                queue.Enqueue(idx);
        }

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            if (!hasRiver[idx])
                continue;

            var localOrder = maxUpstreamOrder[idx] == 0
                ? (byte)1
                : (byte)Math.Clamp(
                    maxUpstreamOrder[idx] + (maxUpstreamOrderCount[idx] >= 2 ? 1 : 0),
                    1,
                    8);
            order[idx] = localOrder;

            var next = downstream[idx];
            if (next < 0 || next >= hasRiver.Length || !hasRiver[next] || next == idx)
                continue;

            if (localOrder > maxUpstreamOrder[next])
            {
                maxUpstreamOrder[next] = localOrder;
                maxUpstreamOrderCount[next] = 1;
            }
            else if (localOrder == maxUpstreamOrder[next] && maxUpstreamOrderCount[next] < byte.MaxValue)
            {
                maxUpstreamOrderCount[next]++;
            }

            indegree[next]--;
            if (indegree[next] == 0)
                queue.Enqueue(next);
        }

        for (var idx = 0; idx < hasRiver.Length; idx++)
        {
            if (!hasRiver[idx] || order[idx] > 0)
                continue;

            var maxUpstream = 0;
            var sameMaxCount = 0;
            var parents = upstream[idx];
            for (var i = 0; i < parents.Count; i++)
            {
                var parentIdx = parents[i];
                if (!hasRiver[parentIdx])
                    continue;

                var parentOrder = order[parentIdx] == 0 ? 1 : order[parentIdx];
                if (parentOrder > maxUpstream)
                {
                    maxUpstream = parentOrder;
                    sameMaxCount = 1;
                }
                else if (parentOrder == maxUpstream)
                {
                    sameMaxCount++;
                }
            }

            order[idx] = maxUpstream <= 0
                ? (byte)1
                : (byte)Math.Clamp(maxUpstream + (sameMaxCount >= 2 ? 1 : 0), 1, 8);
        }

        return order;
    }

    private static List<int>[] BuildUpstreamIndices(int[] downstream)
    {
        var upstream = new List<int>[downstream.Length];
        for (var idx = 0; idx < upstream.Length; idx++)
            upstream[idx] = [];

        for (var idx = 0; idx < downstream.Length; idx++)
        {
            var next = downstream[idx];
            if (next >= 0 && next < downstream.Length && next != idx)
                upstream[next].Add(idx);
        }

        return upstream;
    }

    private static void ApplyRiverValleyShaping(GeneratedRegionMap map)
    {
        var cellCount = map.Width * map.Height;
        var slopeDelta = new float[cellCount];
        var groundwaterDelta = new float[cellCount];
        var soilDelta = new float[cellCount];
        var vegetationDelta = new float[cellCount];
        var resourceDelta = new float[cellCount];

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            if (!tile.HasRiver)
                continue;

            var orderNorm = Math.Clamp(tile.RiverOrder / 8f, 0f, 1f);
            var dischargeNorm = Math.Clamp(tile.RiverDischarge / 12f, 0f, 1f);
            var influence = 0.06f + (orderNorm * 0.10f) + (dischargeNorm * 0.08f);

            foreach (var (dx, dy) in CenterAndCardinalOffsets)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                    continue;

                var distance = Math.Abs(dx) + Math.Abs(dy);
                var falloff = distance switch
                {
                    0 => 1f,
                    1 => 0.58f,
                    _ => 0.32f,
                };

                var delta = influence * falloff;
                var idx = IndexOf(nx, ny, map.Width);
                slopeDelta[idx] = Math.Max(slopeDelta[idx], delta);
                groundwaterDelta[idx] = Math.Max(groundwaterDelta[idx], delta * 0.80f);
                soilDelta[idx] = Math.Max(soilDelta[idx], delta * 0.56f);
                vegetationDelta[idx] = Math.Max(vegetationDelta[idx], delta * 0.34f);
                resourceDelta[idx] = Math.Max(resourceDelta[idx], delta * 0.18f);
            }
        }

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            if (slopeDelta[idx] <= 0f)
                continue;

            var tile = map.GetTile(x, y);
            var slopeReduction = slopeDelta[idx] * 95f;
            var slope = (byte)Math.Clamp((int)MathF.Round(tile.Slope - slopeReduction), 0, 255);
            var groundwater = Math.Clamp(tile.Groundwater + groundwaterDelta[idx], 0f, 1f);
            var soilDepth = Math.Clamp(tile.SoilDepth + soilDelta[idx], 0f, 1f);
            var vegetation = Math.Clamp(tile.VegetationDensity + vegetationDelta[idx], 0f, 1f);
            var resources = Math.Clamp(tile.ResourceRichness - resourceDelta[idx], 0f, 1f);

            map.SetTile(x, y, tile with
            {
                Slope = slope,
                Groundwater = groundwater,
                SoilDepth = soilDepth,
                VegetationDensity = vegetation,
                ResourceRichness = resources,
            });
        }
    }

    private static void ApplyVegetationContinuity(
        GeneratedRegionMap map,
        float[] macroForestCover,
        float[] macroMoisture,
        float[] macroMountainCover)
    {
        var cellCount = map.Width * map.Height;
        var adjusted = new float[cellCount];
        var smoothed = new float[cellCount];

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            var tile = map.GetTile(x, y);
            var neighborhood = AverageVegetation(map, x, y);
            var macroAnchor = Math.Clamp(
                (macroForestCover[idx] * 0.76f) +
                (macroMoisture[idx] * 0.18f) -
                (macroMountainCover[idx] * 0.10f), 0f, 1f);
            var hydrologyBoost = (tile.HasRiver ? 0.06f : 0f) + (tile.HasLake ? 0.08f : 0f);
            var slopePenalty = (tile.Slope / 255f) * 0.14f;

            adjusted[idx] = Math.Clamp(
                (tile.VegetationDensity * 0.30f) +
                (tile.VegetationSuitability * 0.20f) +
                (neighborhood * 0.28f) +
                (macroAnchor * 0.22f) +
                hydrologyBoost -
                slopePenalty, 0f, 1f);
        }

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            var tile = map.GetTile(x, y);
            var neighborhood = AverageScalar(adjusted, map.Width, map.Height, x, y);
            var macroAnchor = Math.Clamp(
                (macroForestCover[idx] * 0.74f) +
                (macroMoisture[idx] * 0.18f) -
                (macroMountainCover[idx] * 0.10f), 0f, 1f);
            var hydrologyBoost = (tile.HasRiver ? 0.04f : 0f) + (tile.HasLake ? 0.06f : 0f);
            var slopePenalty = (tile.Slope / 255f) * 0.12f;

            smoothed[idx] = Math.Clamp(
                (adjusted[idx] * 0.48f) +
                (tile.VegetationSuitability * 0.20f) +
                (neighborhood * 0.16f) +
                (macroAnchor * 0.16f) +
                hydrologyBoost -
                slopePenalty, 0f, 1f);
        }

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            var tile = map.GetTile(x, y);
            var vegetation = Math.Clamp(
                (smoothed[idx] * 0.82f) +
                (tile.VegetationSuitability * 0.18f), 0f, 1f);
            var suitability = Math.Clamp(
                (tile.VegetationSuitability * 0.78f) +
                (vegetation * 0.22f), 0f, 1f);
            var soilDepth = Math.Clamp((tile.SoilDepth * 0.82f) + (vegetation * 0.18f), 0f, 1f);
            var groundwater = Math.Clamp((tile.Groundwater * 0.90f) + (vegetation * 0.10f), 0f, 1f);

            map.SetTile(x, y, tile with
            {
                VegetationDensity = vegetation,
                VegetationSuitability = suitability,
                SoilDepth = soilDepth,
                Groundwater = groundwater,
            });
        }
    }

    private static float AverageVegetation(GeneratedRegionMap map, int x, int y)
    {
        var weighted = 0f;
        var weight = 0f;

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                continue;

            var isCenter = dx == 0 && dy == 0;
            var isCardinal = dx == 0 || dy == 0;
            var sampleWeight = isCenter ? 1.45f : (isCardinal ? 1f : 0.72f);
            weighted += map.GetTile(nx, ny).VegetationDensity * sampleWeight;
            weight += sampleWeight;
        }

        return weight <= 0f ? map.GetTile(x, y).VegetationDensity : (weighted / weight);
    }

    private static float AverageScalar(float[] values, int width, int height, int x, int y)
    {
        var weighted = 0f;
        var weight = 0f;

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;

            var isCenter = dx == 0 && dy == 0;
            var isCardinal = dx == 0 || dy == 0;
            var sampleWeight = isCenter ? 1.45f : (isCardinal ? 1f : 0.72f);
            weighted += values[IndexOf(nx, ny, width)] * sampleWeight;
            weight += sampleWeight;
        }

        return weight <= 0f ? values[IndexOf(x, y, width)] : (weighted / weight);
    }

    private static void ApplyHistoryOverlay(
        GeneratedRegionMap map,
        WorldCoord worldCoord,
        GeneratedWorldHistory? history,
        int regionSeed,
        int worldSeed,
        RegionRoadEdges[] roadBoundaryContracts)
    {
        if (history is null)
            return;

        var roadGenerationEnabled = WorldGenFeatureFlags.EnableRoadGeneration;
        var localSites = history.Sites
            .Where(site => site.Location == worldCoord)
            .ToArray();
        var localRoads = roadGenerationEnabled
            ? history.Roads
                .Where(road => RoadTouchesWorldCoord(road, worldCoord))
                .ToArray()
            : [];
        if (localSites.Length == 0 && localRoads.Length == 0)
            return;

        var siteAnchors = new Dictionary<string, SiteAnchor>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in localSites)
        {
            var (x, y) = ResolveSiteAnchor(map, site, regionSeed);
            siteAnchors[site.Id] = new SiteAnchor(
                site.Id,
                x,
                y,
                Math.Clamp(site.Development, 0f, 1f),
                site.Kind);
        }

        var settlementMask = new bool[map.Width * map.Height];
        var roadMask = roadGenerationEnabled ? new bool[map.Width * map.Height] : null;

        foreach (var anchor in siteAnchors.Values)
            ApplySettlementInfluence(map, settlementMask, anchor);

        if (roadGenerationEnabled && roadMask is not null)
        {
            foreach (var road in localRoads)
                ApplyRoadInfluence(map, roadMask, siteAnchors, road, worldCoord, regionSeed, worldSeed, roadBoundaryContracts);

            var center = (X: map.Width / 2, Y: map.Height / 2);
            foreach (var anchor in siteAnchors.Values)
            {
                if (HasRoadInNeighborhood(roadMask, map.Width, map.Height, anchor.X, anchor.Y, radius: 1))
                    continue;

                TraceManhattanRoad(roadMask, map.Width, map.Height, (anchor.X, anchor.Y), center, horizontalFirst: true);
            }
        }

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var idx = IndexOf(x, y, map.Width);
            var tile = map.GetTile(x, y);
            var canBuild = !tile.HasRiver && !tile.HasLake;
            var hasSettlement = tile.HasSettlement || (canBuild && settlementMask[idx]);
            var hasRoad = roadGenerationEnabled &&
                          roadMask is not null &&
                          (tile.HasRoad || (canBuild && roadMask[idx]));

            if (tile.HasSettlement == hasSettlement && tile.HasRoad == hasRoad)
                continue;

            map.SetTile(x, y, tile with
            {
                HasSettlement = hasSettlement,
                HasRoad = hasRoad,
            });
        }
    }

    private static bool RoadTouchesWorldCoord(RoadRecord road, WorldCoord worldCoord)
    {
        foreach (var coord in road.Path)
        {
            if (coord == worldCoord)
                return true;
        }

        return false;
    }

    private static (int X, int Y) ResolveSiteAnchor(GeneratedRegionMap map, SiteRecord site, int regionSeed)
    {
        var centerX = map.Width / 2;
        var centerY = map.Height / 2;
        var spreadX = Math.Max(2, map.Width / 4);
        var spreadY = Math.Max(2, map.Height / 4);

        var hash = StableHash(site.Id, regionSeed, site.Location.X, site.Location.Y);
        var ox = ((hash & 0xFF) - 127) / 127f;
        var oy = (((hash >> 8) & 0xFF) - 127) / 127f;
        var x = Math.Clamp(centerX + (int)MathF.Round(ox * spreadX), 1, map.Width - 2);
        var y = Math.Clamp(centerY + (int)MathF.Round(oy * spreadY), 1, map.Height - 2);

        if (IsBuildableCell(map.GetTile(x, y)))
            return (x, y);

        var bestX = centerX;
        var bestY = centerY;
        var bestScore = int.MaxValue;
        for (var ny = 1; ny < map.Height - 1; ny++)
        for (var nx = 1; nx < map.Width - 1; nx++)
        {
            if (!IsBuildableCell(map.GetTile(nx, ny)))
                continue;

            var score = Math.Abs(nx - x) + Math.Abs(ny - y);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestX = nx;
            bestY = ny;
        }

        return (bestX, bestY);
    }

    private static bool IsBuildableCell(GeneratedRegionTile tile)
        => !tile.HasRiver && !tile.HasLake;

    private static void ApplySettlementInfluence(GeneratedRegionMap map, bool[] settlementMask, SiteAnchor anchor)
    {
        var baseRadius = 1;
        if (anchor.Kind.Contains("fort", StringComparison.OrdinalIgnoreCase) ||
            anchor.Kind.Contains("capital", StringComparison.OrdinalIgnoreCase))
        {
            baseRadius = 2;
        }

        var radius = Math.Clamp(baseRadius + (int)MathF.Round(anchor.Development * 2f), 1, 4);
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var x = anchor.X + dx;
            var y = anchor.Y + dy;
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                continue;
            if ((dx * dx) + (dy * dy) > (radius * radius))
                continue;
            if (!IsBuildableCell(map.GetTile(x, y)))
                continue;

            settlementMask[IndexOf(x, y, map.Width)] = true;
        }
    }

    private static void ApplyRoadInfluence(
        GeneratedRegionMap map,
        bool[] roadMask,
        IReadOnlyDictionary<string, SiteAnchor> siteAnchors,
        RoadRecord road,
        WorldCoord worldCoord,
        int regionSeed,
        int worldSeed,
        RegionRoadEdges[] roadBoundaryContracts)
    {
        if (road.Path.Count == 0)
            return;

        var first = -1;
        var last = -1;
        for (var i = 0; i < road.Path.Count; i++)
        {
            if (road.Path[i] != worldCoord)
                continue;

            if (first < 0)
                first = i;
            last = i;
        }

        if (first < 0)
            return;

        var entryNeighbor = ResolveNeighborOutsideTile(road.Path, first, step: -1, worldCoord);
        var exitNeighbor = ResolveNeighborOutsideTile(road.Path, last, step: 1, worldCoord);

        siteAnchors.TryGetValue(road.FromSiteId, out var fromAnchor);
        siteAnchors.TryGetValue(road.ToSiteId, out var toAnchor);

        var start = ResolveRoadEndpoint(
            map,
            road.Id,
            fromAnchor,
            entryNeighbor,
            worldCoord,
            regionSeed,
            worldSeed);
        var end = ResolveRoadEndpoint(
            map,
            road.Id,
            toAnchor,
            exitNeighbor,
            worldCoord,
            regionSeed,
            worldSeed);
        if (start.BoundaryEdge != RegionRoadEdges.None)
        {
            var idx = IndexOf(start.X, start.Y, map.Width);
            roadBoundaryContracts[idx] |= start.BoundaryEdge;
        }
        if (end.BoundaryEdge != RegionRoadEdges.None)
        {
            var idx = IndexOf(end.X, end.Y, map.Width);
            roadBoundaryContracts[idx] |= end.BoundaryEdge;
        }

        var horizontalFirst = (StableHash(road.Id, regionSeed, map.Width, map.Height) & 1) == 0;
        TraceManhattanRoad(roadMask, map.Width, map.Height, (start.X, start.Y), (end.X, end.Y), horizontalFirst);
    }

    private static WorldCoord? ResolveNeighborOutsideTile(
        IReadOnlyList<WorldCoord> path,
        int index,
        int step,
        WorldCoord worldCoord)
    {
        var i = index + step;
        while (i >= 0 && i < path.Count)
        {
            var coord = path[i];
            if (coord == worldCoord)
            {
                i += step;
                continue;
            }

            if (Math.Abs(coord.X - worldCoord.X) + Math.Abs(coord.Y - worldCoord.Y) == 1)
                return coord;
            return null;
        }

        return null;
    }

    private static ResolvedRoadEndpoint ResolveRoadEndpoint(
        GeneratedRegionMap map,
        string roadId,
        SiteAnchor anchor,
        WorldCoord? neighbor,
        WorldCoord worldCoord,
        int regionSeed,
        int worldSeed)
    {
        if (neighbor is not null &&
            TryResolveEdgePointForNeighbor(map, worldCoord, neighbor.Value, roadId, worldSeed, out var edgePoint, out var boundaryEdge))
        {
            return new ResolvedRoadEndpoint(edgePoint.X, edgePoint.Y, boundaryEdge);
        }

        if (!string.IsNullOrWhiteSpace(anchor.SiteId))
            return new ResolvedRoadEndpoint(anchor.X, anchor.Y);

        return new ResolvedRoadEndpoint(map.Width / 2, map.Height / 2);
    }

    private static bool TryResolveEdgePointForNeighbor(
        GeneratedRegionMap map,
        WorldCoord worldCoord,
        WorldCoord neighbor,
        string roadId,
        int worldSeed,
        out (int X, int Y) point,
        out RegionRoadEdges boundaryEdge)
    {
        if (neighbor.X == worldCoord.X && neighbor.Y == worldCoord.Y - 1)
        {
            point = (ResolveSharedRoadBoundaryOffset(worldSeed, worldCoord, neighbor, roadId, map.Width), 0);
            boundaryEdge = RegionRoadEdges.North;
            return true;
        }
        if (neighbor.X == worldCoord.X + 1 && neighbor.Y == worldCoord.Y)
        {
            point = (map.Width - 1, ResolveSharedRoadBoundaryOffset(worldSeed, worldCoord, neighbor, roadId, map.Height));
            boundaryEdge = RegionRoadEdges.East;
            return true;
        }
        if (neighbor.X == worldCoord.X && neighbor.Y == worldCoord.Y + 1)
        {
            point = (ResolveSharedRoadBoundaryOffset(worldSeed, worldCoord, neighbor, roadId, map.Width), map.Height - 1);
            boundaryEdge = RegionRoadEdges.South;
            return true;
        }
        if (neighbor.X == worldCoord.X - 1 && neighbor.Y == worldCoord.Y)
        {
            point = (0, ResolveSharedRoadBoundaryOffset(worldSeed, worldCoord, neighbor, roadId, map.Height));
            boundaryEdge = RegionRoadEdges.West;
            return true;
        }

        point = default;
        boundaryEdge = RegionRoadEdges.None;
        return false;
    }

    private static int ResolveSharedRoadBoundaryOffset(
        int worldSeed,
        WorldCoord worldCoord,
        WorldCoord neighbor,
        string roadId,
        int axisSize)
    {
        int keyX;
        int keyY;
        int salt;
        if (worldCoord.X == neighbor.X)
        {
            keyX = worldCoord.X;
            keyY = Math.Min(worldCoord.Y, neighbor.Y);
            salt = 18101;
        }
        else
        {
            keyX = Math.Min(worldCoord.X, neighbor.X);
            keyY = worldCoord.Y;
            salt = 18131;
        }

        return ResolveEdgeOffset(roadId, worldSeed, salt, keyX, keyY, axisSize);
    }

    private static int ResolveEdgeOffset(string id, int seed, int salt, int keyX, int keyY, int axisSize)
    {
        var margin = axisSize > 8 ? 2 : 1;
        var min = margin;
        var max = axisSize - 1 - margin;
        if (max <= min)
            return axisSize / 2;

        var span = max - min + 1;
        var hash = StableHash(id, seed, salt, keyX, keyY);
        var offset = (hash & int.MaxValue) % span;
        return min + offset;
    }

    private static int StableHash(string value, int a, int b, int c)
        => StableHash(value, a, b, c, 0);

    private static int StableHash(string value, int a, int b, int c, int d)
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + a;
            hash = (hash * 31) + b;
            hash = (hash * 31) + c;
            hash = (hash * 31) + d;
            for (var i = 0; i < value.Length; i++)
                hash = (hash * 31) + value[i];
            return hash;
        }
    }

    private static void TraceManhattanRoad(
        bool[] roadMask,
        int width,
        int height,
        (int X, int Y) from,
        (int X, int Y) to,
        bool horizontalFirst)
    {
        if (horizontalFirst)
        {
            TraceLine(roadMask, width, height, from, (to.X, from.Y));
            TraceLine(roadMask, width, height, (to.X, from.Y), to);
        }
        else
        {
            TraceLine(roadMask, width, height, from, (from.X, to.Y));
            TraceLine(roadMask, width, height, (from.X, to.Y), to);
        }
    }

    private static void TraceLine(
        bool[] roadMask,
        int width,
        int height,
        (int X, int Y) from,
        (int X, int Y) to)
    {
        var x = from.X;
        var y = from.Y;
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);

        var steps = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
        for (var i = 0; i <= steps; i++)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
                roadMask[IndexOf(x, y, width)] = true;

            if (x != to.X)
                x += dx;
            else if (y != to.Y)
                y += dy;
        }
    }

    private static bool HasRoadInNeighborhood(bool[] roadMask, int width, int height, int x, int y, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;
            if (!roadMask[IndexOf(nx, ny, width)])
                continue;

            return true;
        }

        return false;
    }

    private readonly record struct SiteAnchor(string SiteId, int X, int Y, float Development, string Kind);
    private readonly record struct ResolvedRoadEndpoint(int X, int Y, RegionRoadEdges BoundaryEdge = RegionRoadEdges.None);

    private static void ReclassifyBiomeVariants(GeneratedRegionMap map)
    {
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map.GetTile(x, y);
            var slopeNorm = tile.Slope / 255f;
            var variant = ResolveBiomeVariantId(
                map.ParentMacroBiomeId,
                tile.VegetationDensity,
                slopeNorm,
                tile.HasRiver,
                tile.HasLake);
            var surfaceClassId = RegionSurfaceResolver.ResolveSurfaceClassId(
                map.ParentMacroBiomeId,
                variant,
                slopeNorm,
                tile.HasRiver,
                tile.HasLake,
                tile.MoistureBand,
                tile.Groundwater,
                tile.TemperatureBand,
                tile.SoilDepth);

            if (string.Equals(variant, tile.BiomeVariantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(surfaceClassId, tile.SurfaceClassId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            map.SetTile(x, y, tile with
            {
                BiomeVariantId = variant,
                SurfaceClassId = surfaceClassId,
            });
        }
    }

    private static RegionRiverEdges ToRegionEdge(BoundaryEdge edge)
        => edge switch
        {
            BoundaryEdge.North => RegionRiverEdges.North,
            BoundaryEdge.East => RegionRiverEdges.East,
            BoundaryEdge.South => RegionRiverEdges.South,
            BoundaryEdge.West => RegionRiverEdges.West,
            _ => RegionRiverEdges.None,
        };

    private static int ResolveSharedBoundaryOffset(int worldSeed, WorldCoord worldCoord, BoundaryEdge edge, int axisSize)
    {
        int keyX;
        int keyY;
        int salt;
        switch (edge)
        {
            case BoundaryEdge.North:
                keyX = worldCoord.X;
                keyY = worldCoord.Y - 1;
                salt = 17011;
                break;
            case BoundaryEdge.South:
                keyX = worldCoord.X;
                keyY = worldCoord.Y;
                salt = 17011;
                break;
            case BoundaryEdge.East:
                keyX = worldCoord.X;
                keyY = worldCoord.Y;
                salt = 17029;
                break;
            case BoundaryEdge.West:
                keyX = worldCoord.X - 1;
                keyY = worldCoord.Y;
                salt = 17029;
                break;
            default:
                keyX = worldCoord.X;
                keyY = worldCoord.Y;
                salt = 17041;
                break;
        }

        var hash = SeedHash.Hash(worldSeed, keyX, keyY, salt);
        var margin = axisSize > 8 ? 2 : 1;
        var min = margin;
        var max = axisSize - 1 - margin;
        if (max <= min)
            return axisSize / 2;

        var span = max - min + 1;
        var offset = Math.Abs(hash % span);
        return min + offset;
    }

    private static int ResolveSharedRoadBoundaryOffset(int worldSeed, WorldCoord worldCoord, BoundaryEdge edge, int axisSize)
    {
        int keyX;
        int keyY;
        int salt;
        switch (edge)
        {
            case BoundaryEdge.North:
                keyX = worldCoord.X;
                keyY = worldCoord.Y - 1;
                salt = 17111;
                break;
            case BoundaryEdge.South:
                keyX = worldCoord.X;
                keyY = worldCoord.Y;
                salt = 17111;
                break;
            case BoundaryEdge.East:
                keyX = worldCoord.X;
                keyY = worldCoord.Y;
                salt = 17129;
                break;
            case BoundaryEdge.West:
                keyX = worldCoord.X - 1;
                keyY = worldCoord.Y;
                salt = 17129;
                break;
            default:
                keyX = worldCoord.X;
                keyY = worldCoord.Y;
                salt = 17141;
                break;
        }

        var hash = SeedHash.Hash(worldSeed, keyX, keyY, salt);
        var margin = axisSize > 8 ? 2 : 1;
        var min = margin;
        var max = axisSize - 1 - margin;
        if (max <= min)
            return axisSize / 2;

        var span = max - min + 1;
        var offset = Math.Abs(hash % span);
        return min + offset;
    }

    private static float ResolveSharedBoundaryDischarge(
        GeneratedWorldMap world,
        WorldCoord worldCoord,
        GeneratedWorldTile parentTile,
        BoundaryEdge edge)
    {
        var neighborX = worldCoord.X;
        var neighborY = worldCoord.Y;
        switch (edge)
        {
            case BoundaryEdge.North:
                neighborY--;
                break;
            case BoundaryEdge.East:
                neighborX++;
                break;
            case BoundaryEdge.South:
                neighborY++;
                break;
            case BoundaryEdge.West:
                neighborX--;
                break;
        }

        var parent = Math.Clamp(parentTile.RiverDischarge, 0f, 12f);
        if (!TryGetNeighbor(world, neighborX, neighborY, out var neighborTile))
            return Math.Clamp(parent <= 0f ? 1f : parent, 1f, 12f);

        var neighbor = Math.Clamp(neighborTile.RiverDischarge, 0f, 12f);
        var shared = Math.Max(parent, neighbor);
        if (shared <= 0f)
            shared = Math.Clamp(Math.Max(parentTile.FlowAccumulation, neighborTile.FlowAccumulation), 1f, 12f);
        return Math.Clamp(shared <= 0f ? 1f : shared, 1f, 12f);
    }

    private static bool[] BuildForcedRiverMask(
        List<BoundaryPortal> portals,
        float[] elevation,
        int width,
        int height,
        int seed)
    {
        var forced = new bool[elevation.Length];
        if (portals.Count == 0)
            return forced;

        var points = new List<(int X, int Y)>(portals.Count);
        foreach (var portal in portals)
            points.Add((portal.X, portal.Y));

        if (points.Count == 1)
        {
            var sink = FindLowlandTarget(elevation, width, height);
            CarveRiverPath(points[0], sink, elevation, forced, width, height, seed);
            return forced;
        }

        var paired = new bool[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            if (paired[i])
                continue;

            var partner = FindNearestUnpaired(points, paired, i);
            if (partner < 0)
            {
                var sink = FindLowlandTarget(elevation, width, height);
                CarveRiverPath(points[i], sink, elevation, forced, width, height, seed + (i * 31));
                paired[i] = true;
                continue;
            }

            CarveRiverPath(points[i], points[partner], elevation, forced, width, height, seed + (i * 31));
            paired[i] = true;
            paired[partner] = true;
        }

        return forced;
    }

    private static bool[] BuildForcedRoadMask(
        RegionRoadEdges[] boundaryContracts,
        float[] elevation,
        float[] ruggedness,
        int width,
        int height,
        int seed)
    {
        var forced = new bool[boundaryContracts.Length];
        var portals = new List<(int X, int Y)>(4);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = IndexOf(x, y, width);
            if (boundaryContracts[idx] == RegionRoadEdges.None)
                continue;

            portals.Add((x, y));
            forced[idx] = true;
        }

        if (portals.Count == 0)
            return forced;

        var hub = FindRoadHub(elevation, ruggedness, width, height);
        if (portals.Count == 1)
        {
            var horizontalFirst = (SeedHash.Hash(seed, portals[0].X, portals[0].Y, 18311) & 1) == 0;
            TraceManhattanRoad(forced, width, height, portals[0], hub, horizontalFirst);
            return forced;
        }

        var paired = new bool[portals.Count];
        for (var i = 0; i < portals.Count; i++)
        {
            if (paired[i])
                continue;

            var partner = FindNearestUnpaired(portals, paired, i);
            var horizontalFirst = (SeedHash.Hash(seed, portals[i].X, portals[i].Y, 18349 + i) & 1) == 0;
            if (partner < 0)
            {
                TraceManhattanRoad(forced, width, height, portals[i], hub, horizontalFirst);
                paired[i] = true;
                continue;
            }

            TraceManhattanRoad(forced, width, height, portals[i], portals[partner], horizontalFirst);
            paired[i] = true;
            paired[partner] = true;
        }

        return forced;
    }

    private static (int X, int Y) FindRoadHub(float[] elevation, float[] ruggedness, int width, int height)
    {
        var centerX = width / 2;
        var centerY = height / 2;
        var bestX = centerX;
        var bestY = centerY;
        var bestScore = float.MaxValue;

        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            var idx = IndexOf(x, y, width);
            var centerDistance = (Math.Abs(x - centerX) / (float)Math.Max(1, width - 1)) +
                                 (Math.Abs(y - centerY) / (float)Math.Max(1, height - 1));
            var score =
                (ruggedness[idx] * 0.55f) +
                (MathF.Max(0f, elevation[idx] - 0.72f) * 0.30f) +
                (MathF.Max(0f, 0.18f - elevation[idx]) * 0.18f) +
                (centerDistance * 0.28f);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestX = x;
            bestY = y;
        }

        return (bestX, bestY);
    }

    private static (int X, int Y) FindLowlandTarget(float[] elevation, int width, int height)
    {
        var bestIdx = 0;
        var best = float.MaxValue;
        for (var idx = 0; idx < elevation.Length; idx++)
        {
            var x = idx % width;
            var y = idx / width;
            if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1)
                continue;

            if (elevation[idx] >= best)
                continue;

            best = elevation[idx];
            bestIdx = idx;
        }

        return (bestIdx % width, bestIdx / width);
    }

    private static int FindNearestUnpaired(List<(int X, int Y)> points, bool[] paired, int index)
    {
        var source = points[index];
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < points.Count; i++)
        {
            if (i == index || paired[i])
                continue;

            var target = points[i];
            var dist = Math.Abs(target.X - source.X) + Math.Abs(target.Y - source.Y);
            if (dist >= bestDistance)
                continue;

            bestDistance = dist;
            best = i;
        }

        return best;
    }

    private static void CarveRiverPath(
        (int X, int Y) source,
        (int X, int Y) target,
        float[] elevation,
        bool[] forcedMask,
        int width,
        int height,
        int seed)
    {
        var visited = new bool[forcedMask.Length];
        var x = source.X;
        var y = source.Y;
        var maxSteps = Math.Max(width + height, 24);

        for (var step = 0; step < maxSteps; step++)
        {
            var idx = IndexOf(x, y, width);
            forcedMask[idx] = true;
            if (visited[idx])
                break;

            visited[idx] = true;
            if (x == target.X && y == target.Y)
                break;

            var bestX = x;
            var bestY = y;
            var bestScore = float.MaxValue;

            foreach (var (dx, dy) in CardinalNeighborOffsets)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var nIdx = IndexOf(nx, ny, width);
                if (visited[nIdx])
                    continue;

                var distance = Math.Abs(nx - target.X) + Math.Abs(ny - target.Y);
                var borderPenalty = (nx == 0 || ny == 0 || nx == width - 1 || ny == height - 1) ? 0.06f : 0f;
                var jitter = SeedHash.Unit(seed, nIdx, idx, 991) * 0.05f;
                var score = (distance * 0.62f) + (elevation[nIdx] * 0.32f) + borderPenalty + jitter;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestX = nx;
                bestY = ny;
            }

            if (bestX == x && bestY == y)
                break;

            x = bestX;
            y = bestY;
        }

        forcedMask[IndexOf(target.X, target.Y, width)] = true;
    }

    private static int[] BuildFlowDirections(float[] elevation, int width, int height, int seed)
    {
        var outlets = BuildRegionOutletMask(width, height);
        var routingElevation = HydrologySolver.BuildFilledElevation(
            elevation,
            width,
            height,
            idx => outlets[idx],
            CardinalNeighborOffsets);

        return HydrologySolver.BuildFlowDirections(
            routingElevation,
            width,
            height,
            seed,
            CardinalNeighborOffsets,
            idx => outlets[idx]);
    }

    private static float[] BuildFlowAccumulation(float[] elevation, float[] moisture, int[] downstream)
    {
        var contribution = new float[elevation.Length];
        for (var idx = 0; idx < contribution.Length; idx++)
        {
            contribution[idx] = 1f +
                                (moisture[idx] * 0.55f) +
                                (MathF.Max(0f, 1f - elevation[idx]) * 0.20f);
        }

        return HydrologySolver.BuildFlowAccumulation(downstream, contribution);
    }

    private static float EstimateSlopeNorm(
        int x,
        int y,
        float[] elevation,
        float[] ruggedness,
        int width,
        int height)
    {
        var idx = IndexOf(x, y, width);
        var min = elevation[idx];
        var max = elevation[idx];

        foreach (var (dx, dy) in SurroundingNeighborOffsets)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                continue;

            var n = elevation[IndexOf(nx, ny, width)];
            if (n < min) min = n;
            if (n > max) max = n;
        }

        return Math.Clamp(((max - min) * 2.8f) + (ruggedness[idx] * 0.15f), 0f, 1f);
    }

    private static int IndexOf(int x, int y, int width)
        => y * width + x;

    private static bool[] BuildRegionOutletMask(int width, int height)
    {
        var outlets = new bool[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                outlets[IndexOf(x, y, width)] = true;
        }

        return outlets;
    }

    private static float SampleWorldScalar(
        GeneratedWorldMap world,
        float worldX,
        float worldY,
        Func<GeneratedWorldTile, float> selector)
    {
        var maxX = world.Width - 1;
        var maxY = world.Height - 1;
        var clampedX = Math.Clamp(worldX, 0f, maxX);
        var clampedY = Math.Clamp(worldY, 0f, maxY);

        var x0 = (int)MathF.Floor(clampedX);
        var y0 = (int)MathF.Floor(clampedY);
        var x1 = Math.Min(maxX, x0 + 1);
        var y1 = Math.Min(maxY, y0 + 1);
        var tx = clampedX - x0;
        var ty = clampedY - y0;

        var v00 = selector(world.GetTile(x0, y0));
        var v10 = selector(world.GetTile(x1, y0));
        var v01 = selector(world.GetTile(x0, y1));
        var v11 = selector(world.GetTile(x1, y1));

        var top = Lerp(v00, v10, tx);
        var bottom = Lerp(v01, v11, tx);
        return Lerp(top, bottom, ty);
    }

    private static float SampleWorldScalarBlended(
        GeneratedWorldMap world,
        float edgeWorldX,
        float edgeWorldY,
        float interiorWorldX,
        float interiorWorldY,
        float interiorBlend,
        Func<GeneratedWorldTile, float> selector)
    {
        var clampedBlend = Math.Clamp(interiorBlend, 0f, 1f);
        var edgeSample = SampleWorldScalar(world, edgeWorldX, edgeWorldY, selector);
        if (clampedBlend <= 0f)
            return edgeSample;

        var interiorSample = SampleWorldScalar(world, interiorWorldX, interiorWorldY, selector);
        if (clampedBlend >= 1f)
            return interiorSample;

        return Lerp(edgeSample, interiorSample, clampedBlend);
    }

    private static float ResolveInteriorBlendWeight(float fx, float fy)
    {
        var distanceToEdge = MathF.Min(
            MathF.Min(fx, 1f - fx),
            MathF.Min(fy, 1f - fy));
        return SmoothStep(0.04f, 0.44f, distanceToEdge);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (edge1 <= edge0)
            return value >= edge1 ? 1f : 0f;

        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float Lerp(float a, float b, float t)
        => a + ((b - a) * t);

    private static float ResolveRegionTemperatureBand(
        float macroTemperature,
        float localElevation,
        float slopeNorm,
        string macroBiomeId)
    {
        var lapseCooling = MathF.Max(0f, localElevation - 0.52f) * 0.26f;
        var ruggedCooling = slopeNorm * 0.06f;
        var biomeBias = macroBiomeId switch
        {
            MacroBiomeIds.Desert => 0.06f,
            MacroBiomeIds.Savanna => 0.04f,
            MacroBiomeIds.TropicalRainforest => 0.03f,
            MacroBiomeIds.BorealForest => -0.05f,
            MacroBiomeIds.Tundra => -0.08f,
            MacroBiomeIds.IcePlains => -0.14f,
            MacroBiomeIds.OceanShallow => 0.02f,
            MacroBiomeIds.OceanDeep => 0.01f,
            _ => 0f,
        };

        return Math.Clamp(macroTemperature + biomeBias - lapseCooling - ruggedCooling, 0f, 1f);
    }

    private static float ResolveTemperatureVegetationSupport(float temperature, float elevation, string macroBiomeId)
    {
        if (MacroBiomeIds.IsOcean(macroBiomeId))
            return 0f;

        var optimal = macroBiomeId switch
        {
            MacroBiomeIds.TropicalRainforest => 0.76f,
            MacroBiomeIds.Desert => 0.80f,
            MacroBiomeIds.Savanna => 0.70f,
            MacroBiomeIds.BorealForest => 0.30f,
            MacroBiomeIds.ConiferForest => 0.38f,
            MacroBiomeIds.Tundra => 0.16f,
            MacroBiomeIds.IcePlains => 0.06f,
            MacroBiomeIds.Highland => 0.44f,
            _ => 0.54f,
        };

        var tolerance = macroBiomeId switch
        {
            MacroBiomeIds.Desert => 0.42f,
            MacroBiomeIds.Savanna => 0.36f,
            MacroBiomeIds.TropicalRainforest => 0.32f,
            MacroBiomeIds.Tundra => 0.24f,
            MacroBiomeIds.IcePlains => 0.18f,
            _ => 0.28f,
        };

        var distance = MathF.Abs(temperature - optimal);
        var baseSupport = 1f - Math.Clamp(distance / tolerance, 0f, 1f);
        var altitudePenalty = MathF.Max(0f, elevation - 0.78f) * 0.45f;
        return Math.Clamp(baseSupport - altitudePenalty, 0f, 1f);
    }

    private static string ResolveBiomeVariantId(
        string macroBiomeId,
        float vegetation,
        float slopeNorm,
        bool hasRiver,
        bool hasLake)
    {
        return macroBiomeId switch
        {
            MacroBiomeIds.OceanDeep => RegionBiomeVariantIds.OpenOcean,
            MacroBiomeIds.OceanShallow => RegionBiomeVariantIds.CoastalShallows,

            MacroBiomeIds.ConiferForest when vegetation >= 0.70f => RegionBiomeVariantIds.DenseConifer,
            MacroBiomeIds.ConiferForest when vegetation >= 0.45f => RegionBiomeVariantIds.ConiferWoodland,
            MacroBiomeIds.ConiferForest => RegionBiomeVariantIds.RockyConiferEdge,

            MacroBiomeIds.BorealForest when vegetation >= 0.65f => RegionBiomeVariantIds.DenseConifer,
            MacroBiomeIds.BorealForest when vegetation >= 0.42f => RegionBiomeVariantIds.ConiferWoodland,
            MacroBiomeIds.BorealForest => RegionBiomeVariantIds.RockyConiferEdge,

            MacroBiomeIds.Highland when slopeNorm >= 0.72f => RegionBiomeVariantIds.AlpineRidge,
            MacroBiomeIds.Highland => RegionBiomeVariantIds.HighlandFoothills,

            MacroBiomeIds.MistyMarsh when hasRiver || hasLake => RegionBiomeVariantIds.FloodplainMarsh,
            MacroBiomeIds.MistyMarsh => RegionBiomeVariantIds.ReedMarsh,

            MacroBiomeIds.TropicalRainforest when hasRiver || hasLake => RegionBiomeVariantIds.FloodplainMarsh,
            MacroBiomeIds.TropicalRainforest when vegetation >= 0.72f => RegionBiomeVariantIds.TropicalCanopy,
            MacroBiomeIds.TropicalRainforest => RegionBiomeVariantIds.TropicalLowland,

            MacroBiomeIds.Desert when hasRiver || hasLake => RegionBiomeVariantIds.SteppeScrub,
            MacroBiomeIds.Desert when slopeNorm >= 0.62f => RegionBiomeVariantIds.AridBadlands,
            MacroBiomeIds.Desert => RegionBiomeVariantIds.DrySteppe,
            MacroBiomeIds.Savanna when hasRiver || hasLake => RegionBiomeVariantIds.RiverValley,
            MacroBiomeIds.Savanna when vegetation >= 0.32f => RegionBiomeVariantIds.SavannaGrassland,
            MacroBiomeIds.Savanna when vegetation <= 0.18f => RegionBiomeVariantIds.DrySteppe,
            MacroBiomeIds.Savanna => RegionBiomeVariantIds.SteppeScrub,

            MacroBiomeIds.WindsweptSteppe when vegetation <= 0.25f => RegionBiomeVariantIds.DrySteppe,
            MacroBiomeIds.WindsweptSteppe => RegionBiomeVariantIds.SteppeScrub,

            MacroBiomeIds.Tundra when hasRiver || hasLake => RegionBiomeVariantIds.RiverValley,
            MacroBiomeIds.Tundra when slopeNorm >= 0.68f => RegionBiomeVariantIds.AlpineRidge,
            MacroBiomeIds.Tundra => RegionBiomeVariantIds.PolarTundra,
            MacroBiomeIds.IcePlains when slopeNorm >= 0.68f => RegionBiomeVariantIds.AlpineRidge,
            MacroBiomeIds.IcePlains => RegionBiomeVariantIds.GlacialField,

            _ when vegetation >= 0.55f => RegionBiomeVariantIds.TemperateWoodland,
            _ => RegionBiomeVariantIds.TemperatePlainsOpen,
        };
    }

}
