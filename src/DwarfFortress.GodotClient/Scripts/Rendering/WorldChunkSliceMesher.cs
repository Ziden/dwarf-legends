using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using Godot;
using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

public sealed class WorldChunkSliceMesher
{
    private const float GroundHeight = 0.18f;
    private const float FloorHeight = 0.22f;
    private const float RampHeight = 0.30f;
    private const float StairHeight = 0.34f;
    private const float WallHeight = 0.72f;
    private const float WaterBaseHeight = 0.05f;
    private const float WaterHeightScale = 0.16f;
    private const float MagmaBaseHeight = 0.08f;
    private const float MagmaHeightScale = 0.20f;
    private const float MinExposedSideHeight = 0.001f;
    private const float TerrainDetailTopOffset = 0.006f;
    private const float TerrainDetailSideOffset = 0.006f;
    private const float SurfaceCornerJitterAmplitude = 0.014f;
    private const float CaveCornerJitterAmplitude = 0.020f;
    private const float SurfaceCornerHeightAmplitude = 0.008f;
    private const float CaveCornerHeightAmplitude = 0.015f;
    private const float LiquidCornerWaveAmplitude = 0.006f;
    private const float LiquidShoreInsetStrength = 0.18f;

    private readonly record struct CornerSample(
        int FlatSurfaceCount,
        int CaveSurfaceCount,
        int WaterCount,
        int MagmaCount,
        Vector2 WaterCentroid,
        Vector2 MagmaCentroid);

    private enum SurfaceWarpStyle
    {
        None,
        Flat,
        Liquid,
    }

    internal ChunkSliceMeshBuild? BuildSliceMesh(
        WorldChunkRenderSnapshot snapshot,
        int currentZ,
        Func<int, int, int, WorldTileData?> tryGetTile,
        WorldChunkSliceRenderCache sliceRenderCache)
    {
        var localZ = currentZ - snapshot.Origin.Z;
        if (!Chunk.IsLocalInBounds(0, 0, localZ))
            return null;

        var topSurfaceBuilder = new TexturedSurfaceBuilder();
        var sideSurfaceBuilder = new ColoredSurfaceBuilder();
        var detailSurfaceBuilder = new TexturedSurfaceBuilder();

        var hasTopGeometry = false;
        var hasSideGeometry = false;
        var hasDetailGeometry = false;

        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            if (!snapshot.TryGetLocalTile(localX, localY, localZ, out var tile) || tile.TileDefId == TileDefIds.Empty)
                continue;

            hasTopGeometry = true;
            var result = AddTileGeometry(
                topSurfaceBuilder,
                sideSurfaceBuilder,
                detailSurfaceBuilder,
                snapshot,
                localX,
                localY,
                tile,
                currentZ,
                tryGetTile,
                sliceRenderCache);
            hasSideGeometry |= result.HasSideGeometry;
            hasDetailGeometry |= result.HasDetailGeometry;
        }

        if (!hasTopGeometry)
            return null;

        var mesh = new ArrayMesh();
        var topSurfaceIndex = mesh.GetSurfaceCount();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, topSurfaceBuilder.BuildMeshArrays());

        int? sideSurfaceIndex = null;
        if (hasSideGeometry)
        {
            sideSurfaceIndex = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, sideSurfaceBuilder.BuildMeshArrays());
        }

        int? detailSurfaceIndex = null;
        if (hasDetailGeometry)
        {
            detailSurfaceIndex = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, detailSurfaceBuilder.BuildMeshArrays());
        }

        return new ChunkSliceMeshBuild(
            mesh,
            topSurfaceIndex,
            sideSurfaceIndex,
            detailSurfaceIndex,
            topSurfaceBuilder.VertexCount,
            sideSurfaceBuilder.VertexCount,
            detailSurfaceBuilder.VertexCount);
    }

    private static TileGeometryResult AddTileGeometry(
        TexturedSurfaceBuilder topSurfaceBuilder,
        ColoredSurfaceBuilder sideSurfaceBuilder,
        TexturedSurfaceBuilder detailSurfaceBuilder,
        WorldChunkRenderSnapshot snapshot,
        int localX,
        int localY,
        WorldTileData tile,
        int currentZ,
        Func<int, int, int, WorldTileData?> tryGetTile,
        WorldChunkSliceRenderCache sliceRenderCache)
    {
        var tileColor = ResolveTileColor(tile);
        var tileHeight = ResolveTileHeight(tile);
        var sideColor = tileColor.Darkened(0.24f);

        var tileSize = WorldRender3D.TileWorldSize;
        var worldX = snapshot.Origin.X + localX;
        var worldY = snapshot.Origin.Y + localY;
        var warpStyle = ResolveSurfaceWarpStyle(tile);
        var northWest = ResolveCornerVertex(snapshot, tryGetTile, worldX, worldY, currentZ, tileHeight, tileSize, warpStyle);
        var northEast = ResolveCornerVertex(snapshot, tryGetTile, worldX + 1, worldY, currentZ, tileHeight, tileSize, warpStyle);
        var southEast = ResolveCornerVertex(snapshot, tryGetTile, worldX + 1, worldY + 1, currentZ, tileHeight, tileSize, warpStyle);
        var southWest = ResolveCornerVertex(snapshot, tryGetTile, worldX, worldY + 1, currentZ, tileHeight, tileSize, warpStyle);

        var hasVisual = sliceRenderCache.TryGet(localX, localY, out var tileVisual);
        var textureLayer = hasVisual ? tileVisual.TerrainLayer : 0;
        var hasDetailLayer = hasVisual && tileVisual.HasDetailLayer;
        var detailLayer = hasVisual ? tileVisual.DetailLayer : -1;
        var renderDetailSides = hasDetailLayer && ShouldRenderTerrainDetailSideOverlay(tile);
        var hasSideGeometry = false;
        var hasDetailGeometry = false;

        var northNeighborHeight = ResolveNeighborHeight(tryGetTile, worldX, worldY - 1, currentZ);
        var eastNeighborHeight = ResolveNeighborHeight(tryGetTile, worldX + 1, worldY, currentZ);
        var southNeighborHeight = ResolveNeighborHeight(tryGetTile, worldX, worldY + 1, currentZ);
        var westNeighborHeight = ResolveNeighborHeight(tryGetTile, worldX - 1, worldY, currentZ);
        var northBase = ResolveFaceBaseVertices(snapshot, tryGetTile, worldX, worldY - 1, currentZ, worldX, worldY, worldX + 1, worldY, tileSize, northWest, northEast);
        var eastBase = ResolveFaceBaseVertices(snapshot, tryGetTile, worldX + 1, worldY, currentZ, worldX + 1, worldY, worldX + 1, worldY + 1, tileSize, northEast, southEast);
        var southBase = ResolveFaceBaseVertices(snapshot, tryGetTile, worldX, worldY + 1, currentZ, worldX + 1, worldY + 1, worldX, worldY + 1, tileSize, southEast, southWest);
        var westBase = ResolveFaceBaseVertices(snapshot, tryGetTile, worldX - 1, worldY, currentZ, worldX, worldY + 1, worldX, worldY, tileSize, southWest, northWest);

        AddTopQuad(topSurfaceBuilder, northWest, northEast, southEast, southWest, textureLayer);
        if (hasDetailLayer)
        {
            AddTexturedQuad(
                detailSurfaceBuilder,
                northWest + (Vector3.Up * TerrainDetailTopOffset),
                northEast + (Vector3.Up * TerrainDetailTopOffset),
                southEast + (Vector3.Up * TerrainDetailTopOffset),
                southWest + (Vector3.Up * TerrainDetailTopOffset),
                detailLayer);
            hasDetailGeometry = true;
        }

        hasSideGeometry |= AddSideFace(sideSurfaceBuilder, northBase.A, northBase.B, northWest, northEast, tileHeight, northNeighborHeight, sideColor);
        hasSideGeometry |= AddSideFace(sideSurfaceBuilder, eastBase.A, eastBase.B, northEast, southEast, tileHeight, eastNeighborHeight, sideColor);
        hasSideGeometry |= AddSideFace(sideSurfaceBuilder, southBase.A, southBase.B, southEast, southWest, tileHeight, southNeighborHeight, sideColor);
        hasSideGeometry |= AddSideFace(sideSurfaceBuilder, westBase.A, westBase.B, southWest, northWest, tileHeight, westNeighborHeight, sideColor);

        if (renderDetailSides)
        {
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceBuilder, northBase.A, northBase.B, northWest, northEast, tileHeight, northNeighborHeight, new Vector3(0f, 0f, -TerrainDetailSideOffset), detailLayer);
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceBuilder, eastBase.A, eastBase.B, northEast, southEast, tileHeight, eastNeighborHeight, new Vector3(TerrainDetailSideOffset, 0f, 0f), detailLayer);
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceBuilder, southBase.A, southBase.B, southEast, southWest, tileHeight, southNeighborHeight, new Vector3(0f, 0f, TerrainDetailSideOffset), detailLayer);
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceBuilder, westBase.A, westBase.B, southWest, northWest, tileHeight, westNeighborHeight, new Vector3(-TerrainDetailSideOffset, 0f, 0f), detailLayer);
        }

        return new TileGeometryResult(hasSideGeometry, hasDetailGeometry);
    }

    private static void AddTopQuad(TexturedSurfaceBuilder surfaceBuilder, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int textureLayer)
    {
        AddTexturedQuad(surfaceBuilder, a, b, c, d, textureLayer);
    }

    private static void AddTexturedQuad(TexturedSurfaceBuilder surfaceBuilder, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int textureLayer)
    {
        surfaceBuilder.AddQuad(a, b, c, d, textureLayer);
    }

    private static bool AddSideFace(
        ColoredSurfaceBuilder surfaceBuilder,
        Vector3 baseA,
        Vector3 baseB,
        Vector3 topA,
        Vector3 topB,
        float tileHeight,
        float neighborHeight,
        Color color)
    {
        if (neighborHeight >= tileHeight - MinExposedSideHeight)
            return false;

        AddQuad(surfaceBuilder, baseA, baseB, topB, topA, color);
        return true;
    }

    private static bool AddDetailSideFace(
        TexturedSurfaceBuilder surfaceBuilder,
        Vector3 baseA,
        Vector3 baseB,
        Vector3 topA,
        Vector3 topB,
        float tileHeight,
        float neighborHeight,
        Vector3 offset,
        int textureLayer)
    {
        if (neighborHeight >= tileHeight - MinExposedSideHeight)
            return false;

        AddTexturedQuad(surfaceBuilder, baseA + offset, baseB + offset, topB + offset, topA + offset, textureLayer);
        return true;
    }

    private static (Vector3 A, Vector3 B) ResolveFaceBaseVertices(
        WorldChunkRenderSnapshot snapshot,
        Func<int, int, int, WorldTileData?> tryGetTile,
        int neighborTileX,
        int neighborTileY,
        int worldZ,
        int cornerAX,
        int cornerAY,
        int cornerBX,
        int cornerBY,
        float tileSize,
        Vector3 fallbackA,
        Vector3 fallbackB)
    {
        var neighbor = tryGetTile(neighborTileX, neighborTileY, worldZ);
        if (neighbor is not { } resolvedNeighbor || resolvedNeighbor.TileDefId == TileDefIds.Empty)
            return (new Vector3(fallbackA.X, 0f, fallbackA.Z), new Vector3(fallbackB.X, 0f, fallbackB.Z));

        var neighborHeight = ResolveTileHeight(resolvedNeighbor);
        var neighborWarpStyle = ResolveSurfaceWarpStyle(resolvedNeighbor);
        return (
            ResolveCornerVertex(snapshot, tryGetTile, cornerAX, cornerAY, worldZ, neighborHeight, tileSize, neighborWarpStyle),
            ResolveCornerVertex(snapshot, tryGetTile, cornerBX, cornerBY, worldZ, neighborHeight, tileSize, neighborWarpStyle));
    }

    private static float ResolveNeighborHeight(Func<int, int, int, WorldTileData?> tryGetTile, int worldX, int worldY, int worldZ)
    {
        var neighbor = tryGetTile(worldX, worldY, worldZ);
        if (neighbor is not { } resolvedNeighbor)
            return 0f;

        return resolvedNeighbor.TileDefId == TileDefIds.Empty ? 0f : ResolveTileHeight(resolvedNeighbor);
    }

    private static Vector3 ResolveCornerVertex(
        WorldChunkRenderSnapshot snapshot,
        Func<int, int, int, WorldTileData?> tryGetTile,
        int gridWorldX,
        int gridWorldY,
        int worldZ,
        float tileHeight,
        float tileSize,
        SurfaceWarpStyle warpStyle)
    {
        var baseX = (gridWorldX - snapshot.Origin.X) * tileSize;
        var baseZ = (gridWorldY - snapshot.Origin.Y) * tileSize;
        if (warpStyle == SurfaceWarpStyle.None)
            return new Vector3(baseX, tileHeight, baseZ);

        var sample = SampleCorner(tryGetTile, gridWorldX, gridWorldY, worldZ);
        var horizontalOffset = ResolveCornerHorizontalOffset(sample, gridWorldX, gridWorldY, worldZ);
        var verticalOffset = warpStyle switch
        {
            SurfaceWarpStyle.Flat => ResolveFlatCornerVerticalOffset(sample, gridWorldX, gridWorldY, worldZ),
            SurfaceWarpStyle.Liquid => ResolveLiquidCornerVerticalOffset(sample, gridWorldX, gridWorldY, worldZ),
            _ => 0f,
        };

        return new Vector3(baseX + horizontalOffset.X, tileHeight + verticalOffset, baseZ + horizontalOffset.Y);
    }

    private static SurfaceWarpStyle ResolveSurfaceWarpStyle(WorldTileData tile)
    {
        if (IsWaterTile(tile) || IsMagmaTile(tile))
            return SurfaceWarpStyle.Liquid;

        if (SupportsFlatSurfaceWarp(tile))
            return SurfaceWarpStyle.Flat;

        return SurfaceWarpStyle.None;
    }

    private static bool SupportsFlatSurfaceWarp(WorldTileData tile)
    {
        if (!tile.IsPassable)
            return false;
        if (tile.TileDefId == TileDefIds.Empty || tile.TileDefId == TileDefIds.Tree)
            return false;
        if (IsWaterTile(tile) || IsMagmaTile(tile))
            return false;

        return tile.TileDefId is not TileDefIds.Ramp and not TileDefIds.Staircase;
    }

    private static bool IsCaveLikeFlatSurface(WorldTileData tile, int worldZ)
    {
        if (worldZ <= 0)
            return false;

        return tile.TileDefId is TileDefIds.StoneFloor or TileDefIds.Obsidian;
    }

    private static CornerSample SampleCorner(Func<int, int, int, WorldTileData?> tryGetTile, int gridWorldX, int gridWorldY, int worldZ)
    {
        var flatSurfaceCount = 0;
        var caveSurfaceCount = 0;
        var waterCount = 0;
        var magmaCount = 0;
        var waterCentroid = Vector2.Zero;
        var magmaCentroid = Vector2.Zero;

        SampleCornerTile(tryGetTile, gridWorldX - 1, gridWorldY - 1, worldZ, new Vector2(-0.5f, -0.5f), ref flatSurfaceCount, ref caveSurfaceCount, ref waterCount, ref magmaCount, ref waterCentroid, ref magmaCentroid);
        SampleCornerTile(tryGetTile, gridWorldX, gridWorldY - 1, worldZ, new Vector2(0.5f, -0.5f), ref flatSurfaceCount, ref caveSurfaceCount, ref waterCount, ref magmaCount, ref waterCentroid, ref magmaCentroid);
        SampleCornerTile(tryGetTile, gridWorldX - 1, gridWorldY, worldZ, new Vector2(-0.5f, 0.5f), ref flatSurfaceCount, ref caveSurfaceCount, ref waterCount, ref magmaCount, ref waterCentroid, ref magmaCentroid);
        SampleCornerTile(tryGetTile, gridWorldX, gridWorldY, worldZ, new Vector2(0.5f, 0.5f), ref flatSurfaceCount, ref caveSurfaceCount, ref waterCount, ref magmaCount, ref waterCentroid, ref magmaCentroid);

        if (waterCount > 0)
            waterCentroid /= waterCount;
        if (magmaCount > 0)
            magmaCentroid /= magmaCount;

        return new CornerSample(flatSurfaceCount, caveSurfaceCount, waterCount, magmaCount, waterCentroid, magmaCentroid);
    }

    private static void SampleCornerTile(
        Func<int, int, int, WorldTileData?> tryGetTile,
        int worldX,
        int worldY,
        int worldZ,
        Vector2 center,
        ref int flatSurfaceCount,
        ref int caveSurfaceCount,
        ref int waterCount,
        ref int magmaCount,
        ref Vector2 waterCentroid,
        ref Vector2 magmaCentroid)
    {
        var tile = tryGetTile(worldX, worldY, worldZ);
        if (tile is not { } resolvedTile)
            return;

        if (resolvedTile.TileDefId == TileDefIds.Empty)
            return;

        if (SupportsFlatSurfaceWarp(resolvedTile))
        {
            flatSurfaceCount++;
            if (IsCaveLikeFlatSurface(resolvedTile, worldZ))
                caveSurfaceCount++;
        }

        if (IsWaterTile(resolvedTile))
        {
            waterCount++;
            waterCentroid += center;
        }
        else if (IsMagmaTile(resolvedTile))
        {
            magmaCount++;
            magmaCentroid += center;
        }
    }

    private static Vector2 ResolveCornerHorizontalOffset(CornerSample sample, int gridWorldX, int gridWorldY, int worldZ)
    {
        var supportCount = sample.FlatSurfaceCount + sample.WaterCount + sample.MagmaCount;
        if (supportCount <= 0)
            return Vector2.Zero;

        var jitterVector = ResolveSignedCornerVectorNoise(gridWorldX, gridWorldY, worldZ, salt: 41);
        var caveWeight = sample.FlatSurfaceCount <= 0 ? 0f : sample.CaveSurfaceCount / (float)sample.FlatSurfaceCount;
        var jitterAmplitude = Mathf.Lerp(SurfaceCornerJitterAmplitude, CaveCornerJitterAmplitude, caveWeight) * Math.Clamp(supportCount / 4f, 0.35f, 1f);
        var offset = jitterVector * jitterAmplitude;

        if (sample.WaterCount > 0 && sample.WaterCount < 4 && sample.MagmaCount == 0)
            offset += sample.WaterCentroid * ResolveLiquidInsetScale(sample.WaterCount);
        else if (sample.MagmaCount > 0 && sample.MagmaCount < 4 && sample.WaterCount == 0)
            offset += sample.MagmaCentroid * ResolveLiquidInsetScale(sample.MagmaCount);

        return offset;
    }

    private static float ResolveFlatCornerVerticalOffset(CornerSample sample, int gridWorldX, int gridWorldY, int worldZ)
    {
        if (sample.FlatSurfaceCount <= 0)
            return 0f;

        var caveWeight = sample.CaveSurfaceCount <= 0 ? 0f : sample.CaveSurfaceCount / (float)sample.FlatSurfaceCount;
        var amplitude = Mathf.Lerp(SurfaceCornerHeightAmplitude, CaveCornerHeightAmplitude, caveWeight) * Math.Clamp(sample.FlatSurfaceCount / 4f, 0.40f, 1f);
        return ResolveSignedCornerNoise(gridWorldX, gridWorldY, worldZ, salt: 67) * amplitude;
    }

    private static float ResolveLiquidCornerVerticalOffset(CornerSample sample, int gridWorldX, int gridWorldY, int worldZ)
    {
        var liquidCount = sample.WaterCount > 0 ? sample.WaterCount : sample.MagmaCount;
        if (liquidCount <= 0)
            return 0f;

        var shoreWeight = liquidCount < 4 ? 1f : 0.45f;
        return ResolveSignedCornerNoise(gridWorldX, gridWorldY, worldZ, salt: 83) * LiquidCornerWaveAmplitude * shoreWeight;
    }

    private static float ResolveLiquidInsetScale(int liquidCount)
    {
        return liquidCount switch
        {
            1 => LiquidShoreInsetStrength,
            2 => LiquidShoreInsetStrength * 0.82f,
            3 => LiquidShoreInsetStrength * 0.45f,
            _ => 0f,
        };
    }

    private static float ResolveSignedCornerNoise(int gridWorldX, int gridWorldY, int worldZ, int salt)
    {
        var waveA = MathF.Sin((gridWorldX * 0.61f) + (gridWorldY * 1.17f) + (worldZ * 0.83f) + (salt * 0.19f));
        var waveB = MathF.Sin((gridWorldX * 1.41f) - (gridWorldY * 0.47f) + (worldZ * 0.29f) + (salt * 0.11f));
        return ((waveA * 0.62f) + (waveB * 0.38f)) * 0.5f;
    }

    private static Vector2 ResolveSignedCornerVectorNoise(int gridWorldX, int gridWorldY, int worldZ, int salt)
    {
        var x = ResolveSignedCornerNoise(gridWorldX, gridWorldY, worldZ, salt);
        var y = ResolveSignedCornerNoise(gridWorldX, gridWorldY, worldZ, salt + 23);
        var vector = new Vector2(x, y);
        return vector.LengthSquared() <= 0.0001f ? Vector2.Zero : vector.Normalized();
    }

    private static bool IsWaterTile(WorldTileData tile)
        => tile.FluidType == FluidType.Water || tile.TileDefId == TileDefIds.Water;

    private static bool IsMagmaTile(WorldTileData tile)
        => tile.FluidType == FluidType.Magma || tile.TileDefId == TileDefIds.Magma;

    private static float ResolveTileHeight(WorldTileData tile)
    {
        if (tile.TileDefId == TileDefIds.Tree)
            return GroundHeight;

        if (IsWaterTile(tile))
            return WaterBaseHeight + ((tile.FluidLevel / 7f) * WaterHeightScale);

        if (IsMagmaTile(tile))
            return MagmaBaseHeight + ((tile.FluidLevel / 7f) * MagmaHeightScale);

        return tile.TileDefId switch
        {
            TileDefIds.Ramp => RampHeight,
            TileDefIds.Staircase => StairHeight,
            TileDefIds.StoneFloor or TileDefIds.Obsidian or TileDefIds.WoodFloor or TileDefIds.StoneBrick => FloorHeight,
            _ when !tile.IsPassable => WallHeight,
            _ => GroundHeight,
        };
    }

    private static Color ResolveTileColor(WorldTileData tile)
    {
        if (IsWaterTile(tile))
            return new Color(0.18f, 0.47f, 0.78f);

        if (IsMagmaTile(tile))
            return new Color(0.93f, 0.33f, 0.08f);

        return tile.TileDefId switch
        {
            TileDefIds.Grass => new Color(0.31f, 0.58f, 0.23f),
            TileDefIds.Sand => new Color(0.79f, 0.71f, 0.47f),
            TileDefIds.Mud => new Color(0.42f, 0.31f, 0.19f),
            TileDefIds.Soil or TileDefIds.SoilWall => new Color(0.48f, 0.36f, 0.22f),
            TileDefIds.Tree => new Color(0.31f, 0.58f, 0.23f),
            TileDefIds.WoodFloor => new Color(0.54f, 0.38f, 0.22f),
            TileDefIds.Obsidian => new Color(0.16f, 0.12f, 0.19f),
            _ when !tile.IsPassable => ResolveStoneColor(tile.MaterialId, new Color(0.43f, 0.43f, 0.44f)),
            _ => ResolveStoneColor(tile.MaterialId, new Color(0.52f, 0.49f, 0.42f)),
        };
    }

    private static Color ResolveStoneColor(string? materialId, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return fallback;

        if (materialId.Contains("granite", StringComparison.OrdinalIgnoreCase))
            return new Color(0.46f, 0.47f, 0.49f);
        if (materialId.Contains("limestone", StringComparison.OrdinalIgnoreCase))
            return new Color(0.67f, 0.64f, 0.56f);
        if (materialId.Contains("sandstone", StringComparison.OrdinalIgnoreCase))
            return new Color(0.63f, 0.52f, 0.36f);
        if (materialId.Contains("basalt", StringComparison.OrdinalIgnoreCase))
            return new Color(0.29f, 0.30f, 0.33f);
        if (materialId.Contains("slate", StringComparison.OrdinalIgnoreCase))
            return new Color(0.31f, 0.33f, 0.37f);
        if (materialId.Contains("marble", StringComparison.OrdinalIgnoreCase))
            return new Color(0.77f, 0.75f, 0.73f);

        return fallback;
    }

    private static bool ShouldRenderTerrainDetailSideOverlay(WorldTileData tile)
    {
        if (string.IsNullOrWhiteSpace(tile.OreItemDefId))
            return false;

        return tile.TileDefId == TileDefIds.StoneWall
            || tile.TileDefId == TileDefIds.SoilWall
            || !tile.IsPassable;
    }

    private static void AddQuad(ColoredSurfaceBuilder surfaceBuilder, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        surfaceBuilder.AddQuad(a, b, c, d, color);
    }

    public readonly record struct ChunkSliceMeshBuild(
        ArrayMesh Mesh,
        int TopSurfaceIndex,
        int? SideSurfaceIndex,
        int? DetailSurfaceIndex,
        int TopVertexCount,
        int SideVertexCount,
        int DetailVertexCount);

    private readonly record struct TileGeometryResult(
        bool HasSideGeometry,
        bool HasDetailGeometry);

    private sealed class TexturedSurfaceBuilder
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<Vector2> _uv2 = new();

        public int VertexCount => _vertices.Count;

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int textureLayer)
        {
            var layer = new Vector2(textureLayer, 0f);
            AddVertex(a, new Vector2(0f, 0f), layer);
            AddVertex(b, new Vector2(1f, 0f), layer);
            AddVertex(c, new Vector2(1f, 1f), layer);
            AddVertex(a, new Vector2(0f, 0f), layer);
            AddVertex(c, new Vector2(1f, 1f), layer);
            AddVertex(d, new Vector2(0f, 1f), layer);
        }

        public Godot.Collections.Array BuildMeshArrays()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            return arrays;
        }

        private void AddVertex(Vector3 vertex, Vector2 uv, Vector2 uv2)
        {
            _vertices.Add(vertex);
            _uv.Add(uv);
            _uv2.Add(uv2);
        }
    }

    private sealed class ColoredSurfaceBuilder
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color> _colors = new();

        public int VertexCount => _vertices.Count;

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            AddVertex(a, color);
            AddVertex(b, color);
            AddVertex(c, color);
            AddVertex(a, color);
            AddVertex(c, color);
            AddVertex(d, color);
        }

        public Godot.Collections.Array BuildMeshArrays()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
            return arrays;
        }

        private void AddVertex(Vector3 vertex, Color color)
        {
            _vertices.Add(vertex);
            _colors.Add(color);
        }
    }
}
