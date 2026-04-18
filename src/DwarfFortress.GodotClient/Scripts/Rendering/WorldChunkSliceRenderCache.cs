using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

internal sealed class WorldChunkSliceRenderCache
{
    private readonly WorldChunkSliceTileVisual?[] _entries = new WorldChunkSliceTileVisual?[Chunk.Width * Chunk.Height];
    private readonly Dictionary<(int X, int Y, int Z), TileRenderData?> _tileRenderDataCache = new();
    private readonly Dictionary<(int X, int Y, int Z), GroundVisualData?> _displayGroundCache = new();
    private readonly Func<int, int, int, WorldTileData?> _tryGetTile;
    private readonly Func<string?, string?>? _resolveGroundFromMaterial;
    private readonly int _currentZ;

    private WorldChunkSliceRenderCache(
        Func<int, int, int, WorldTileData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        int currentZ)
    {
        _tryGetTile = tryGetTile;
        _resolveGroundFromMaterial = resolveGroundFromMaterial;
        _currentZ = currentZ;
    }

    public static WorldChunkSliceRenderCache Build(
        WorldChunkRenderSnapshot snapshot,
        int currentZ,
        Func<int, int, int, WorldTileData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial)
    {
        var cache = new WorldChunkSliceRenderCache(tryGetTile, resolveGroundFromMaterial, currentZ);
        cache.Populate(snapshot);
        return cache;
    }

    public bool TryGet(int localX, int localY, out WorldChunkSliceTileVisual tileVisual)
    {
        if (localX < 0 || localX >= Chunk.Width || localY < 0 || localY >= Chunk.Height)
        {
            tileVisual = default;
            return false;
        }

        var entry = _entries[Index(localX, localY)];
        if (entry is not WorldChunkSliceTileVisual resolved)
        {
            tileVisual = default;
            return false;
        }

        tileVisual = resolved;
        return true;
    }

    private void Populate(WorldChunkRenderSnapshot snapshot)
    {
        var localZ = _currentZ - snapshot.Origin.Z;
        if (!Chunk.IsLocalInBounds(0, 0, localZ))
            return;

        for (var localY = 0; localY < Chunk.Height; localY++)
        for (var localX = 0; localX < Chunk.Width; localX++)
        {
            if (!snapshot.TryGetLocalTile(localX, localY, localZ, out var tile) || tile.TileDefId == TileDefIds.Empty)
                continue;

            var worldX = snapshot.Origin.X + localX;
            var worldY = snapshot.Origin.Y + localY;
            var renderData = CreateRenderData(tile);
            var displayGround = TryGetDisplayGround(worldX, worldY, _currentZ);
            var groundTransitions = TerrainSurfaceRecipeBuilder.ResolveGroundTransitions(
                renderData,
                worldX,
                worldY,
                _currentZ,
                TryGetTileRenderData,
                _resolveGroundFromMaterial,
                TryGetDisplayGround,
                displayGround);
            var recipe = TerrainSurfaceRecipeBuilder.Build(
                renderData,
                worldX,
                worldY,
                _currentZ,
                TryGetTileRenderData,
                _resolveGroundFromMaterial,
                groundTransitions,
                displayGround,
                TryGetDisplayGround);
            var terrainLayer = TerrainSurfaceArrayLibrary.GetOrCreateArrayLayer(recipe);
            var hasDetailLayer = TerrainDetailOverlayLibrary.TryGetOrCreateArrayLayer(renderData, worldX, worldY, _currentZ, out var detailLayer);

            _entries[Index(localX, localY)] = new WorldChunkSliceTileVisual(
                renderData,
                displayGround,
                groundTransitions,
                terrainLayer,
                hasDetailLayer,
                detailLayer);
        }
    }

    private TileRenderData? TryGetTileRenderData(int x, int y, int z)
    {
        var key = (x, y, z);
        if (_tileRenderDataCache.TryGetValue(key, out var cached))
            return cached;

        var tile = _tryGetTile(x, y, z);
        TileRenderData? resolved = tile is not { } worldTile || worldTile.TileDefId == TileDefIds.Empty
            ? null
            : CreateRenderData(worldTile);
        _tileRenderDataCache[key] = resolved;
        return resolved;
    }

    private GroundVisualData? TryGetDisplayGround(int x, int y, int z)
    {
        var key = (x, y, z);
        if (_displayGroundCache.TryGetValue(key, out var cached))
            return cached;

        var tile = TryGetTileRenderData(x, y, z);
        GroundVisualData? resolved = tile is TileRenderData resolvedTile
            ? TerrainGroundResolver.ResolveDisplayGround(resolvedTile, x, y, z, TryGetTileRenderData, _resolveGroundFromMaterial)
            : null;
        _displayGroundCache[key] = resolved;
        return resolved;
    }

    private static TileRenderData CreateRenderData(WorldTileData tile)
        => new(tile.TileDefId, tile.MaterialId, tile.FluidType, tile.FluidLevel, tile.FluidMaterialId, tile.OreItemDefId, tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel);

    private static int Index(int localX, int localY)
        => (localY * Chunk.Width) + localX;
}

internal readonly record struct WorldChunkSliceTileVisual(
    TileRenderData Tile,
    GroundVisualData? DisplayGround,
    TerrainTransitionSet? GroundTransitions,
    int TerrainLayer,
    bool HasDetailLayer,
    int DetailLayer);
