using System;
using System.Collections.Generic;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

internal static class TerrainSurfaceCanvasCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<TerrainSurfaceRecipe, Texture2D> Entries = new();

    public static Texture2D GetOrCreateTexture(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions)
    {
        var recipe = TerrainSurfaceRecipeBuilder.Build(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
        lock (Sync)
        {
            return GetOrCreateTexture(recipe);
        }
    }

    public static bool ContainsRecipe(TerrainSurfaceRecipe recipe)
    {
        lock (Sync)
            return Entries.ContainsKey(recipe);
    }

    private static Texture2D GetOrCreateTexture(TerrainSurfaceRecipe recipe)
    {
        if (Entries.TryGetValue(recipe, out var existing))
            return existing;

        var image = TerrainSurfaceRecipeBuilder.ComposeImage(recipe);
        var texture = ImageTexture.CreateFromImage(image);
        Entries[recipe] = texture;
        return texture;
    }
}
