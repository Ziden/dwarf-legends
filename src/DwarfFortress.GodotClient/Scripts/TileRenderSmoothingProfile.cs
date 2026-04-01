using System;
using Godot;

public readonly record struct TileRenderSmoothingContext(
    CanvasItem Canvas,
    Rect2 Rect,
    TileRenderData Tile,
    int X,
    int Y,
    int Z,
    Func<int, int, int, TileRenderData?> TryGetTile,
    Func<string?, string?>? ResolveGroundFromMaterial,
    Func<int, int, int, TerrainTransitionSet?>? TryGetGroundTransitions);

public readonly record struct TileRenderSmoothingProfile(
    string Name,
    Func<TileRenderSmoothingContext, bool> Applies,
    Action<TileRenderSmoothingContext> Draw);