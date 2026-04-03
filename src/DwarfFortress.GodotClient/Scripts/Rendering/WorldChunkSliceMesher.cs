using System;
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

    public ChunkSliceMeshBuild? BuildSliceMesh(
        WorldChunkRenderSnapshot snapshot,
        WorldMap map,
        int currentZ,
        Func<int, int, int, TileRenderData?> tryGetTileRenderData,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        var localZ = currentZ - snapshot.Origin.Z;
        if (!Chunk.IsLocalInBounds(0, 0, localZ))
            return null;

        var topSurfaceTool = new SurfaceTool();
        topSurfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var sideSurfaceTool = new SurfaceTool();
        sideSurfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var detailSurfaceTool = new SurfaceTool();
        detailSurfaceTool.Begin(Mesh.PrimitiveType.Triangles);

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
                topSurfaceTool,
                sideSurfaceTool,
                detailSurfaceTool,
                map,
                snapshot,
                localX,
                localY,
                localZ,
                tile,
                currentZ,
                tryGetTileRenderData,
                resolveGroundFromMaterial);
            hasSideGeometry |= result.HasSideGeometry;
            hasDetailGeometry |= result.HasDetailGeometry;
        }

        if (!hasTopGeometry)
            return null;

        var mesh = new ArrayMesh();
        var topSurfaceIndex = mesh.GetSurfaceCount();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, topSurfaceTool.CommitToArrays());

        int? sideSurfaceIndex = null;
        if (hasSideGeometry)
        {
            sideSurfaceIndex = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, sideSurfaceTool.CommitToArrays());
        }

        int? detailSurfaceIndex = null;
        if (hasDetailGeometry)
        {
            detailSurfaceIndex = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, detailSurfaceTool.CommitToArrays());
        }

        return new ChunkSliceMeshBuild(mesh, topSurfaceIndex, sideSurfaceIndex, detailSurfaceIndex);
    }

    private static TileGeometryResult AddTileGeometry(
        SurfaceTool topSurfaceTool,
        SurfaceTool sideSurfaceTool,
        SurfaceTool detailSurfaceTool,
        WorldMap map,
        WorldChunkRenderSnapshot snapshot,
        int localX,
        int localY,
        int localZ,
        WorldTileData tile,
        int currentZ,
        Func<int, int, int, TileRenderData?> tryGetTileRenderData,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        var tileColor = ResolveTileColor(tile);
        var tileHeight = ResolveTileHeight(tile);
        var sideColor = tileColor.Darkened(0.24f);

        var tileSize = WorldRender3D.TileWorldSize;
        var x0 = localX * tileSize;
        var x1 = (localX + 1) * tileSize;
        var z0 = localY * tileSize;
        var z1 = (localY + 1) * tileSize;

        var northWest = new Vector3(x0, tileHeight, z0);
        var northEast = new Vector3(x1, tileHeight, z0);
        var southEast = new Vector3(x1, tileHeight, z1);
        var southWest = new Vector3(x0, tileHeight, z1);

        var worldX = snapshot.Origin.X + localX;
        var worldY = snapshot.Origin.Y + localY;
        var tileRenderData = tryGetTileRenderData(worldX, worldY, currentZ)
            ?? new TileRenderData(tile.TileDefId, tile.MaterialId, tile.FluidType, tile.FluidLevel, tile.FluidMaterialId, tile.OreItemDefId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel);
        var textureLayer = TileSurfaceLibrary.GetOrCreateArrayLayer(
            tileRenderData,
            worldX,
            worldY,
            currentZ,
            tryGetTileRenderData,
            resolveGroundFromMaterial);
        var hasDetailLayer = TerrainDetailOverlayLibrary.TryGetOrCreateArrayLayer(tileRenderData, worldX, worldY, currentZ, out var detailLayer);
        var renderDetailSides = hasDetailLayer && ShouldRenderTerrainDetailSideOverlay(tile);
        var hasSideGeometry = false;
        var hasDetailGeometry = false;

        var northNeighborHeight = ResolveNeighborHeight(map, worldX, worldY - 1, currentZ);
        var eastNeighborHeight = ResolveNeighborHeight(map, worldX + 1, worldY, currentZ);
        var southNeighborHeight = ResolveNeighborHeight(map, worldX, worldY + 1, currentZ);
        var westNeighborHeight = ResolveNeighborHeight(map, worldX - 1, worldY, currentZ);

        AddTopQuad(topSurfaceTool, northWest, northEast, southEast, southWest, textureLayer);
        if (hasDetailLayer)
        {
            AddTexturedQuad(
                detailSurfaceTool,
                northWest + (Vector3.Up * TerrainDetailTopOffset),
                northEast + (Vector3.Up * TerrainDetailTopOffset),
                southEast + (Vector3.Up * TerrainDetailTopOffset),
                southWest + (Vector3.Up * TerrainDetailTopOffset),
                detailLayer);
            hasDetailGeometry = true;
        }

        hasSideGeometry |= AddSideFace(sideSurfaceTool, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), northWest, northEast, tileHeight, northNeighborHeight, sideColor);
        hasSideGeometry |= AddSideFace(sideSurfaceTool, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), northEast, southEast, tileHeight, eastNeighborHeight, sideColor);
        hasSideGeometry |= AddSideFace(sideSurfaceTool, new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), southEast, southWest, tileHeight, southNeighborHeight, sideColor);
        hasSideGeometry |= AddSideFace(sideSurfaceTool, new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), southWest, northWest, tileHeight, westNeighborHeight, sideColor);

        if (renderDetailSides)
        {
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceTool, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), northWest, northEast, tileHeight, northNeighborHeight, new Vector3(0f, 0f, -TerrainDetailSideOffset), detailLayer);
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceTool, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), northEast, southEast, tileHeight, eastNeighborHeight, new Vector3(TerrainDetailSideOffset, 0f, 0f), detailLayer);
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceTool, new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), southEast, southWest, tileHeight, southNeighborHeight, new Vector3(0f, 0f, TerrainDetailSideOffset), detailLayer);
            hasDetailGeometry |= AddDetailSideFace(detailSurfaceTool, new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), southWest, northWest, tileHeight, westNeighborHeight, new Vector3(-TerrainDetailSideOffset, 0f, 0f), detailLayer);
        }

        return new TileGeometryResult(hasSideGeometry, hasDetailGeometry);
    }

    private static void AddTopQuad(SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int textureLayer)
    {
        AddTexturedQuad(surfaceTool, a, b, c, d, textureLayer);
    }

    private static void AddTexturedQuad(SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int textureLayer)
    {
        var layer = new Vector2(textureLayer, 0f);

        surfaceTool.SetUV(new Vector2(0f, 0f));
        surfaceTool.SetUV2(layer);
        surfaceTool.AddVertex(a);
        surfaceTool.SetUV(new Vector2(1f, 0f));
        surfaceTool.SetUV2(layer);
        surfaceTool.AddVertex(b);
        surfaceTool.SetUV(new Vector2(1f, 1f));
        surfaceTool.SetUV2(layer);
        surfaceTool.AddVertex(c);

        surfaceTool.SetUV(new Vector2(0f, 0f));
        surfaceTool.SetUV2(layer);
        surfaceTool.AddVertex(a);
        surfaceTool.SetUV(new Vector2(1f, 1f));
        surfaceTool.SetUV2(layer);
        surfaceTool.AddVertex(c);
        surfaceTool.SetUV(new Vector2(0f, 1f));
        surfaceTool.SetUV2(layer);
        surfaceTool.AddVertex(d);
    }

    private static bool AddSideFace(
        SurfaceTool surfaceTool,
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

        baseA.Y = neighborHeight;
        baseB.Y = neighborHeight;
        AddQuad(surfaceTool, baseA, baseB, topB, topA, color);
        return true;
    }

    private static bool AddDetailSideFace(
        SurfaceTool surfaceTool,
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

        baseA.Y = neighborHeight;
        baseB.Y = neighborHeight;
        AddTexturedQuad(surfaceTool, baseA + offset, baseB + offset, topB + offset, topA + offset, textureLayer);
        return true;
    }

    private static float ResolveNeighborHeight(WorldMap map, int worldX, int worldY, int worldZ)
    {
        var position = new Vec3i(worldX, worldY, worldZ);
        if (!map.IsInBounds(position))
            return 0f;

        var neighbor = map.GetTile(position);
        return neighbor.TileDefId == TileDefIds.Empty ? 0f : ResolveTileHeight(neighbor);
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

    private static void AddQuad(SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(a);
        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(b);
        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(c);

        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(a);
        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(c);
        surfaceTool.SetColor(color);
        surfaceTool.AddVertex(d);
    }

    public readonly record struct ChunkSliceMeshBuild(
        ArrayMesh Mesh,
        int TopSurfaceIndex,
        int? SideSurfaceIndex,
        int? DetailSurfaceIndex);

    private readonly record struct TileGeometryResult(
        bool HasSideGeometry,
        bool HasDetailGeometry);
}
