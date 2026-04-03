using System;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GodotClient.Rendering;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct TileRenderData(
    string TileDefId,
    string? MaterialId,
    FluidType FluidType = FluidType.None,
    byte FluidLevel = 0,
    string? FluidMaterialId = null,
    string? OreItemDefId = null,
    string? PlantDefId = null,
    byte PlantGrowthStage = 0,
    byte PlantYieldLevel = 0,
    byte PlantSeedLevel = 0,
    bool IsDamp = false,
    bool IsWarm = false);

public static class TileRenderHelper
{
    public static void DrawTile(
        CanvasItem canvas,
        Rect2 rect,
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial = null,
        Func<int, int, int, TerrainTransitionSet?>? tryGetGroundTransitions = null)
    {
        var surfaceTexture = TileSurfaceLibrary.GetOrCreateTexture(
            tile,
            x,
            y,
            z,
            tryGetTile,
            resolveGroundFromMaterial,
            tryGetGroundTransitions?.Invoke(x, y, z));
        canvas.DrawTextureRect(surfaceTexture, rect, false);

        if (tile.TileDefId != TileDefIds.Tree)
            DrawPlantOverlay(canvas, rect, tile);

        DrawOreOverlay(canvas, rect, tile.OreItemDefId, x, y, z);
        DrawDampOverlay(canvas, rect, tile);
        DrawWarmOverlay(canvas, rect, tile);
    }

    private static void DrawPlantOverlay(CanvasItem canvas, Rect2 rect, TileRenderData tile)
    {
        if (!WorldSpriteVisuals.TryPlantOverlay(tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel, out var visual))
            return;

        canvas.DrawTextureRect(visual.Texture, rect, false);
    }

    private static void DrawDampOverlay(CanvasItem canvas, Rect2 rect, TileRenderData tile)
    {
        if (!tile.IsDamp)
            return;

        canvas.DrawRect(rect, PixelArtFactory.GetAquiferOverlayColor(0.16f));
        canvas.DrawRect(rect.Grow(-2f), PixelArtFactory.GetAquiferOverlayColor(0.09f), false, 2f);
    }

    private static void DrawWarmOverlay(CanvasItem canvas, Rect2 rect, TileRenderData tile)
    {
        if (!tile.IsWarm)
            return;

        canvas.DrawRect(rect, new Color(0.95f, 0.36f, 0.05f, 0.12f));
        canvas.DrawRect(rect.Grow(-3f), new Color(0.98f, 0.70f, 0.20f, 0.35f), false, 2f);
    }

    private static void DrawOreOverlay(CanvasItem canvas, Rect2 rect, string? oreItemDefId, int x, int y, int z)
    {
        OreOverlayRenderer.Draw(canvas, rect, oreItemDefId, x, y, z);
    }
}
