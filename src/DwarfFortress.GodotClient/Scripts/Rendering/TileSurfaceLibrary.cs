using System;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class TileSurfaceLibrary
{
    public static Texture2D GetOrCreateTexture(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions = null)
        => TerrainSurfaceCanvasCache.GetOrCreateTexture(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);

    public static int GetOrCreateArrayLayer(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions = null)
    {
        var recipe = TerrainSurfaceRecipeBuilder.Build(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
        return TerrainSurfaceArrayLibrary.GetOrCreateArrayLayer(recipe);
    }

    public static Texture2DArray? GetTextureArray()
        => TerrainSurfaceArrayLibrary.GetTextureArray();

    public static int GetArrayLayerCount()
        => TerrainSurfaceArrayLibrary.GetLayerCount();

    public static int GetArrayCapacity()
        => TerrainSurfaceArrayLibrary.GetCapacity();

    public static int GetArrayRebuildCount()
        => TerrainSurfaceArrayLibrary.GetArrayRebuildCount();

    public static bool DebugHasCanvasTextureForRecipe(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions = null)
    {
        var recipe = TerrainSurfaceRecipeBuilder.Build(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
        return TerrainSurfaceCanvasCache.ContainsRecipe(recipe);
    }

    public static bool DebugHasArrayRecipe(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions = null)
    {
        var recipe = TerrainSurfaceRecipeBuilder.Build(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
        return TerrainSurfaceArrayLibrary.ContainsRecipe(recipe);
    }

    public static void DebugEnsureArrayCapacityForTesting(int requiredLayerCount)
        => TerrainSurfaceArrayLibrary.DebugEnsureCapacityForTesting(requiredLayerCount);
}
