using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

public sealed class WorldSliceHoverResolver3D
{
    private const int CandidateSearchRadius = 2;

    public bool TryResolveHoveredTile(Camera3D? camera, Viewport viewport, WorldMap? map, ChunkPreviewStreamingService? chunkPreviewStreaming, int currentZ, out Vector2I tile)
    {
        tile = default;
        if (camera is null || (map is null && chunkPreviewStreaming is null))
            return false;

        WorldTileData? TryGetTile(int x, int y, int z)
        {
            var position = new Vec3i(x, y, z);
            if (chunkPreviewStreaming?.TryGetTileForRendering(position, out var streamedTile) == true)
                return streamedTile.TileDefId == TileDefIds.Empty ? null : streamedTile;

            if (map is null || !map.IsInBounds(position))
                return null;

            var mapTile = map.GetTile(position);
            return mapTile.TileDefId == TileDefIds.Empty ? null : mapTile;
        }

        var mousePos = viewport.GetMousePosition();
        var rayOrigin = camera.ProjectRayOrigin(mousePos);
        var rayDirection = camera.ProjectRayNormal(mousePos);
        if (Mathf.IsZeroApprox(rayDirection.Y))
            return false;

        var planeY = WorldTileHeightResolver3D.ResolveSliceY(currentZ, surfaceOffset: 0.18f);
        var distance = (planeY - rayOrigin.Y) / rayDirection.Y;
        if (distance < 0f)
            return false;

        var hit = rayOrigin + (rayDirection * distance);
        var coarseX = Mathf.FloorToInt(hit.X);
        var coarseY = Mathf.FloorToInt(hit.Z);

        if (TryResolveTopSurfaceTile(currentZ, rayOrigin, rayDirection, coarseX, coarseY, TryGetTile, out tile))
            return true;

        if (TryGetTile(coarseX, coarseY, currentZ) is not { })
            return false;

        tile = new Vector2I(coarseX, coarseY);
        return true;
    }

    private static bool TryResolveTopSurfaceTile(
        int currentZ,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        int coarseX,
        int coarseY,
        Func<int, int, int, WorldTileData?> tryGetTile,
        out Vector2I tile)
    {
        tile = default;
        var bestDistance = float.PositiveInfinity;
        var found = false;

        var minX = coarseX - CandidateSearchRadius;
        var maxX = coarseX + CandidateSearchRadius;
        var minY = coarseY - CandidateSearchRadius;
        var maxY = coarseY + CandidateSearchRadius;

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            if (tryGetTile(x, y, currentZ) is not { } worldTile)
                continue;

            var topY = WorldTileHeightResolver3D.ResolveSurfaceY(currentZ, worldTile);
            var distance = (topY - rayOrigin.Y) / rayDirection.Y;
            if (distance < 0f || distance >= bestDistance)
                continue;

            var hit = rayOrigin + (rayDirection * distance);
            if (hit.X < x || hit.X >= x + 1f || hit.Z < y || hit.Z >= y + 1f)
                continue;

            bestDistance = distance;
            tile = new Vector2I(x, y);
            found = true;
        }

        return found;
    }
}
