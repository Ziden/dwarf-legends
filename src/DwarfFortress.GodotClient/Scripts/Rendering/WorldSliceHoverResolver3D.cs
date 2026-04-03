using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public sealed class WorldSliceHoverResolver3D
{
    private const int CandidateSearchRadius = 2;

    public bool TryResolveHoveredTile(Camera3D? camera, Viewport viewport, WorldMap? map, int currentZ, out Vector2I tile)
    {
        tile = default;
        if (camera is null || map is null)
            return false;

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

        if (TryResolveTopSurfaceTile(map, currentZ, rayOrigin, rayDirection, coarseX, coarseY, out tile))
            return true;

        if (coarseX < 0 || coarseY < 0 || coarseX >= map.Width || coarseY >= map.Height)
            return false;

        tile = new Vector2I(coarseX, coarseY);
        return true;
    }

    private static bool TryResolveTopSurfaceTile(
        WorldMap map,
        int currentZ,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        int coarseX,
        int coarseY,
        out Vector2I tile)
    {
        tile = default;
        var bestDistance = float.PositiveInfinity;
        var found = false;

        var minX = Mathf.Max(0, coarseX - CandidateSearchRadius);
        var maxX = Mathf.Min(map.Width - 1, coarseX + CandidateSearchRadius);
        var minY = Mathf.Max(0, coarseY - CandidateSearchRadius);
        var maxY = Mathf.Min(map.Height - 1, coarseY + CandidateSearchRadius);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var position = new Vec3i(x, y, currentZ);
            var worldTile = map.GetTile(position);
            if (worldTile.TileDefId == TileDefIds.Empty)
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
