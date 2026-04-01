using System;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.World;
using Godot;

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
    byte PlantSeedLevel = 0);

public static class TileRenderHelper
{
    private static readonly TileRenderSmoothingProfile[] SmoothingProfiles =
    [
        new("tree", AppliesTreeSmoothing, DrawTreeTile),
        new("liquid", AppliesLiquidSmoothing, DrawLiquidTile),
        new("natural-ground", AppliesNaturalGroundSmoothing, DrawNaturalGroundTile),
    ];

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
        var context = new TileRenderSmoothingContext(
            canvas,
            rect,
            tile,
            x,
            y,
            z,
            tryGetTile,
            resolveGroundFromMaterial,
            tryGetGroundTransitions);

        foreach (var profile in SmoothingProfiles)
        {
            if (!profile.Applies(context))
                continue;

            profile.Draw(context);
            return;
        }

        DrawDefaultTile(context);
    }

    private static bool AppliesTreeSmoothing(TileRenderSmoothingContext context)
        => context.Tile.TileDefId == TileDefIds.Tree;

    private static void DrawTreeTile(TileRenderSmoothingContext context)
    {
        var ground = TerrainGroundResolver.ResolveTreeGroundVisual(
            context.X,
            context.Y,
            context.Z,
            context.TryGetTile,
            context.ResolveGroundFromMaterial);
        context.Canvas.DrawTextureRect(PixelArtFactory.GetTile(ground.TileDefId, ground.MaterialId), context.Rect, false);

        var transitions = context.TryGetGroundTransitions?.Invoke(context.X, context.Y, context.Z)
            ?? TerrainTransitionResolver.Resolve(ground, context.X, context.Y, context.Z, context.TryGetTile, context.ResolveGroundFromMaterial);
        TerrainTransitionPatchRenderer.DrawTransitions(context.Canvas, context.Rect, context.X, context.Y, context.Z, transitions);
        DrawOreOverlay(context.Canvas, context.Rect, context.Tile.OreItemDefId, context.X, context.Y, context.Z);
    }

    private static bool AppliesLiquidSmoothing(TileRenderSmoothingContext context)
        => ResolveDisplayedLiquidTileDefId(context.Tile) is not null;

    private static void DrawLiquidTile(TileRenderSmoothingContext context)
    {
        var liquidTileDefId = ResolveDisplayedLiquidTileDefId(context.Tile);
        if (liquidTileDefId is null)
            return;

        var ground = TerrainGroundResolver.ResolveLiquidGroundVisual(
            context.X,
            context.Y,
            context.Z,
            context.Tile,
            context.TryGetTile,
            context.ResolveGroundFromMaterial);
        context.Canvas.DrawTextureRect(PixelArtFactory.GetTile(ground.TileDefId, ground.MaterialId), context.Rect, false);
        var liquidGroundTransitions = context.TryGetGroundTransitions?.Invoke(context.X, context.Y, context.Z)
            ?? TerrainTransitionResolver.Resolve(ground, context.X, context.Y, context.Z, context.TryGetTile, context.ResolveGroundFromMaterial);
        TerrainTransitionPatchRenderer.DrawTransitions(context.Canvas, context.Rect, context.X, context.Y, context.Z, liquidGroundTransitions);

        var depthAlpha = Mathf.Clamp(0.48f + context.Tile.FluidLevel * 0.04f, 0.48f, 0.72f);
        var waterColor = liquidTileDefId == TileDefIds.Magma
            ? new Color(0.95f, 0.38f, 0.05f, depthAlpha)
            : new Color(0.18f, 0.44f, 0.90f, depthAlpha);
        context.Canvas.DrawRect(context.Rect, waterColor);

        var liquidNeighbors = ResolveLiquidNeighborState(liquidTileDefId, context.X, context.Y, context.Z, context.TryGetTile);
        LiquidTransitionPatchRenderer.DrawLiquidTransitions(
            context.Canvas,
            context.Rect,
            liquidTileDefId,
            context.Tile.FluidLevel,
            context.X,
            context.Y,
            context.Z,
            liquidNeighbors);
        DrawPlantOverlay(context.Canvas, context.Rect, context.Tile);
        DrawOreOverlay(context.Canvas, context.Rect, context.Tile.OreItemDefId, context.X, context.Y, context.Z);
    }

    private static bool AppliesNaturalGroundSmoothing(TileRenderSmoothingContext context)
        => TerrainGroundResolver.IsNaturalBlendTileDefId(context.Tile.TileDefId);

    private static void DrawNaturalGroundTile(TileRenderSmoothingContext context)
    {
        context.Canvas.DrawTextureRect(PixelArtFactory.GetTile(context.Tile.TileDefId, context.Tile.MaterialId), context.Rect, false);

        var baseGround = TerrainGroundResolver.ResolveGroundVisual(context.Tile, context.ResolveGroundFromMaterial);
        if (baseGround is not { } groundVisual)
        {
            DrawPlantOverlay(context.Canvas, context.Rect, context.Tile);
            DrawOreOverlay(context.Canvas, context.Rect, context.Tile.OreItemDefId, context.X, context.Y, context.Z);
            return;
        }

        var groundTransitions = context.TryGetGroundTransitions?.Invoke(context.X, context.Y, context.Z)
            ?? TerrainTransitionResolver.Resolve(groundVisual, context.X, context.Y, context.Z, context.TryGetTile, context.ResolveGroundFromMaterial);
        TerrainTransitionPatchRenderer.DrawTransitions(context.Canvas, context.Rect, context.X, context.Y, context.Z, groundTransitions);
        DrawPlantOverlay(context.Canvas, context.Rect, context.Tile);
        DrawOreOverlay(context.Canvas, context.Rect, context.Tile.OreItemDefId, context.X, context.Y, context.Z);
    }

    private static void DrawDefaultTile(TileRenderSmoothingContext context)
    {
        context.Canvas.DrawTextureRect(PixelArtFactory.GetTile(context.Tile.TileDefId, context.Tile.MaterialId), context.Rect, false);
        DrawPlantOverlay(context.Canvas, context.Rect, context.Tile);
        DrawOreOverlay(context.Canvas, context.Rect, context.Tile.OreItemDefId, context.X, context.Y, context.Z);
    }

    private static void DrawPlantOverlay(CanvasItem canvas, Rect2 rect, TileRenderData tile)
    {
        if (string.IsNullOrWhiteSpace(tile.PlantDefId))
            return;

        canvas.DrawTextureRect(
            PixelArtFactory.GetPlantOverlay(tile.PlantDefId, tile.PlantGrowthStage, tile.PlantYieldLevel, tile.PlantSeedLevel),
            rect,
            false);
    }

    private static string? ResolveDisplayedLiquidTileDefId(TileRenderData tile)
        => tile.FluidType switch
        {
            FluidType.Water when tile.FluidLevel > 0 => TileDefIds.Water,
            FluidType.Magma when tile.FluidLevel > 0 => TileDefIds.Magma,
            _ when tile.TileDefId is TileDefIds.Water or TileDefIds.Magma => tile.TileDefId,
            _ => null,
        };

    private static LiquidNeighborState ResolveLiquidNeighborState(
        string liquidTileDefId,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile)
    {
        var matches = TileSmoothingResolver.ResolveMatches(
            x,
            y,
            z,
            tryGetTile,
            new TileMatchRuleProfile<TileRenderData>(tile => IsSameLiquid(liquidTileDefId, tile)));

        return new LiquidNeighborState(
            NorthSame: matches.NorthMatch,
            SouthSame: matches.SouthMatch,
            WestSame: matches.WestMatch,
            EastSame: matches.EastMatch);
    }

    private static bool IsSameLiquid(string liquidTileDefId, TileRenderData? tile)
    {
        if (tile is null)
            return false;

        var displayedLiquidTileDefId = ResolveDisplayedLiquidTileDefId(tile.Value);
        return string.Equals(displayedLiquidTileDefId, liquidTileDefId, StringComparison.Ordinal);
    }

    private static void DrawOreOverlay(CanvasItem canvas, Rect2 rect, string? oreItemDefId, int x, int y, int z)
    {
        if (string.IsNullOrWhiteSpace(oreItemDefId))
            return;

        var (fill, highlight, shadow) = ResolveOrePalette(oreItemDefId);
        var state = BuildOreSeed(oreItemDefId, x, y, z);
        var clusterCount = 2 + (int)(Next01(ref state) * 3f);
        var clusterRadius = Mathf.Clamp(rect.Size.X * 0.055f, 2f, 5f);
        var xPadding = rect.Size.X * 0.18f;
        var yPadding = rect.Size.Y * 0.18f;

        for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
        {
            var center = new Vector2(
                rect.Position.X + xPadding + (rect.Size.X - 2f * xPadding) * Next01(ref state),
                rect.Position.Y + yPadding + (rect.Size.Y - 2f * yPadding) * Next01(ref state));
            var nodeCount = 2 + (int)(Next01(ref state) * 3f);

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                var jitter = new Vector2(
                    (Next01(ref state) - 0.5f) * clusterRadius * 1.9f,
                    (Next01(ref state) - 0.5f) * clusterRadius * 1.9f);
                var nodeCenter = center + jitter;
                var rx = clusterRadius * Mathf.Lerp(0.55f, 1.05f, Next01(ref state));
                var ry = clusterRadius * Mathf.Lerp(0.45f, 0.95f, Next01(ref state));
                var blobRect = new Rect2(nodeCenter - new Vector2(rx, ry), new Vector2(rx * 2f, ry * 2f));

                canvas.DrawRect(blobRect.Grow(0.8f), shadow);
                canvas.DrawRect(blobRect, fill);
                var highlightRect = new Rect2(
                    blobRect.Position + new Vector2(blobRect.Size.X * 0.18f, blobRect.Size.Y * 0.18f),
                    new Vector2(blobRect.Size.X * 0.46f, blobRect.Size.Y * 0.40f));
                canvas.DrawRect(highlightRect, highlight);
            }
        }
    }

    private static (Color Fill, Color Highlight, Color Shadow) ResolveOrePalette(string oreItemDefId)
    {
        var fill = oreItemDefId switch
        {
            ItemDefIds.IronOre => new Color(0.70f, 0.72f, 0.78f, 0.92f),
            ItemDefIds.CopperOre => new Color(0.79f, 0.46f, 0.25f, 0.92f),
            ItemDefIds.CoalOre => new Color(0.36f, 0.36f, 0.40f, 0.92f),
            ItemDefIds.TinOre => new Color(0.75f, 0.78f, 0.82f, 0.92f),
            ItemDefIds.SilverOre => new Color(0.86f, 0.88f, 0.92f, 0.92f),
            ItemDefIds.GoldOre => new Color(0.95f, 0.82f, 0.32f, 0.92f),
            _ => new Color(0.74f, 0.74f, 0.76f, 0.90f),
        };

        return (fill, fill.Lightened(0.22f), fill.Darkened(0.66f));
    }

    private static uint BuildOreSeed(string oreItemDefId, int x, int y, int z)
    {
        unchecked
        {
            uint seed = 2166136261u;
            seed = (seed ^ (uint)x) * 16777619u;
            seed = (seed ^ (uint)y) * 16777619u;
            seed = (seed ^ (uint)z) * 16777619u;

            for (var i = 0; i < oreItemDefId.Length; i++)
                seed = (seed ^ oreItemDefId[i]) * 16777619u;

            return seed;
        }
    }

    private static float Next01(ref uint state)
    {
        unchecked
        {
            state += 0x9E3779B9u;
            var value = state;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
