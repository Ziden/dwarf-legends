using System;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct TileTransitionRuleProfile<TTile, TValue>(
    Func<TTile?, TValue?> ResolveNeighborValue,
    Func<TValue, bool> CanSmoothBase,
    Func<TValue, TValue, bool> ShouldBlendNeighbor,
    Func<TValue, TValue, int, int, int, TerrainCornerQuadrant, TValue> ResolveCorner)
    where TTile : struct
    where TValue : struct;

public readonly record struct TileMatchRuleProfile<TTile>(Func<TTile?, bool> IsMatch)
    where TTile : struct;
