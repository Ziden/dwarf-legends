using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public sealed record StructureMeshParts(ArrayMesh? BodyMesh, ArrayMesh? RoofMesh, bool HideRoofOnHover);

public sealed class WorldStructureMesher
{
    public StructureMeshParts BuildStructureMeshes(BuildingDef definition, PlacedBuildingData building)
    {
        var profile = definition.VisualProfile;
        if (definition.Footprint.Count == 0 || profile is null)
            return new StructureMeshParts(null, null, false);

        if (string.Equals(profile.Archetype, BuildingVisualArchetypes.Hut, System.StringComparison.OrdinalIgnoreCase))
            return BuildHutMeshes(definition, building.Rotation, profile);

        if (string.Equals(profile.Archetype, BuildingVisualArchetypes.Workshop, System.StringComparison.OrdinalIgnoreCase))
            return BuildWorkshopMeshes(definition, profile, building.Rotation);

        return new StructureMeshParts(null, null, false);
    }

    private static StructureMeshParts BuildWorkshopMeshes(BuildingDef definition, BuildingVisualProfile profile, BuildingRotation rotation)
    {
        var bounds = BuildingPlacementGeometry.GetRotatedBounds(definition, rotation);
        var minX = bounds.MinX;
        var maxX = bounds.MaxX + 1;
        var minY = bounds.MinY;
        var maxY = bounds.MaxY + 1;

        var bodyTool = new SurfaceTool();
        bodyTool.Begin(Mesh.PrimitiveType.Triangles);

        var roofTool = new SurfaceTool();
        roofTool.Begin(Mesh.PrimitiveType.Triangles);

        var (bodyColor, roofColor, accentColor) = ResolveWorkshopColors(profile.Palette);
        AddBox(bodyTool, minX, maxX, minY, maxY, 0.02f, 0.62f, bodyColor, bodyColor.Darkened(0.22f));

        var accentSide = BuildingPlacementGeometry.RotateDirection(Vec2i.North, rotation);
        var centerX = (minX + maxX) * 0.5f;
        var centerY = (minY + maxY) * 0.5f;
        switch (accentSide)
        {
            case var side when side == Vec2i.North:
                AddBox(bodyTool, centerX - 0.12f, centerX + 0.12f, minY + 0.12f, minY + 0.22f, 0.10f, 0.42f, accentColor, accentColor.Darkened(0.12f));
                break;
            case var side when side == Vec2i.South:
                AddBox(bodyTool, centerX - 0.12f, centerX + 0.12f, maxY - 0.22f, maxY - 0.12f, 0.10f, 0.42f, accentColor, accentColor.Darkened(0.12f));
                break;
            case var side when side == Vec2i.East:
                AddBox(bodyTool, maxX - 0.22f, maxX - 0.12f, centerY - 0.12f, centerY + 0.12f, 0.10f, 0.42f, accentColor, accentColor.Darkened(0.12f));
                break;
            case var side when side == Vec2i.West:
                AddBox(bodyTool, minX + 0.12f, minX + 0.22f, centerY - 0.12f, centerY + 0.12f, 0.10f, 0.42f, accentColor, accentColor.Darkened(0.12f));
                break;
        }

        AddBox(roofTool, minX + 0.08f, maxX - 0.08f, minY + 0.08f, maxY - 0.08f, 0.62f, 0.82f, roofColor, roofColor.Darkened(0.18f));

        return new StructureMeshParts(bodyTool.Commit(), roofTool.Commit(), HideRoofOnHover: false);
    }

    private static StructureMeshParts BuildHutMeshes(BuildingDef definition, BuildingRotation rotation, BuildingVisualProfile profile)
    {
        var bounds = BuildingPlacementGeometry.GetRotatedBounds(definition, rotation);
        var bodyTool = new SurfaceTool();
        bodyTool.Begin(Mesh.PrimitiveType.Triangles);

        var roofTool = new SurfaceTool();
        roofTool.Begin(Mesh.PrimitiveType.Triangles);

        var (floorColor, floorSeam, wallColor, trimColor, roofColor, roofHighlight, doorColor) = ResolveHutColors(profile.Palette);
        var wallShadow = wallColor.Darkened(0.22f);

        AddBox(bodyTool, bounds.MinX, bounds.MaxX + 1, bounds.MinY, bounds.MaxY + 1, 0.02f, 0.07f, floorColor, floorColor.Darkened(0.18f));
        foreach (var tile in BuildingPlacementGeometry.EnumerateRotatedTiles(definition, rotation))
        {
            var tint = ((tile.Offset.X + tile.Offset.Y) & 1) == 0
                ? floorColor.Lightened(0.07f)
                : floorColor.Darkened(0.04f);
            AddBox(bodyTool, tile.Offset.X + 0.13f, tile.Offset.X + 0.87f, tile.Offset.Y + 0.13f, tile.Offset.Y + 0.87f, 0.07f, 0.085f, tint, tint.Darkened(0.12f));
            AddBox(bodyTool, tile.Offset.X + 0.48f, tile.Offset.X + 0.52f, tile.Offset.Y + 0.16f, tile.Offset.Y + 0.84f, 0.085f, 0.092f, floorSeam, floorSeam);
        }

        var wallThickness = 0.12f;
        var wallBase = 0.07f;
        var wallTop = 0.70f;
        var doorHeight = 0.50f;
        var entry = BuildingPlacementGeometry.GetEntryPoints(definition, Vec3i.Zero, rotation).FirstOrDefault();
        var doorTile = entry.TilePosition;

        foreach (var tile in BuildingPlacementGeometry.EnumerateRotatedTiles(definition, rotation))
        {
            var cellMinX = tile.Offset.X;
            var cellMaxX = tile.Offset.X + 1f;
            var cellMinY = tile.Offset.Y;
            var cellMaxY = tile.Offset.Y + 1f;
            var isDoorTile = doorTile == new Vec3i(tile.Offset.X, tile.Offset.Y, 0);

            if (tile.Offset.Y == bounds.MinY)
                AddBox(bodyTool, cellMinX, cellMaxX, cellMinY, cellMinY + wallThickness, wallBase, wallTop, wallColor, wallShadow);

            if (tile.Offset.Y == bounds.MaxY && !isDoorTile)
                AddBox(bodyTool, cellMinX, cellMaxX, cellMaxY - wallThickness, cellMaxY, wallBase, wallTop, wallColor, wallShadow);

            if (tile.Offset.X == bounds.MinX)
                AddBox(bodyTool, cellMinX, cellMinX + wallThickness, cellMinY, cellMaxY, wallBase, wallTop, wallColor, wallShadow);

            if (tile.Offset.X == bounds.MaxX)
                AddBox(bodyTool, cellMaxX - wallThickness, cellMaxX, cellMinY, cellMaxY, wallBase, wallTop, wallColor, wallShadow);
        }

        AddBox(bodyTool, bounds.MinX, bounds.MinX + 0.18f, bounds.MinY, bounds.MinY + 0.18f, wallBase, wallTop + 0.08f, trimColor, trimColor.Darkened(0.16f));
        AddBox(bodyTool, bounds.MaxX + 0.82f, bounds.MaxX + 1, bounds.MinY, bounds.MinY + 0.18f, wallBase, wallTop + 0.08f, trimColor, trimColor.Darkened(0.16f));
        AddBox(bodyTool, bounds.MinX, bounds.MinX + 0.18f, bounds.MaxY + 0.82f, bounds.MaxY + 1, wallBase, wallTop + 0.08f, trimColor, trimColor.Darkened(0.16f));
        AddBox(bodyTool, bounds.MaxX + 0.82f, bounds.MaxX + 1, bounds.MaxY + 0.82f, bounds.MaxY + 1, wallBase, wallTop + 0.08f, trimColor, trimColor.Darkened(0.16f));

        if (entry != default)
        {
            if (entry.OutwardDirection == Vec3i.South)
            {
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + 1f, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, doorHeight, wallTop, wallColor, wallShadow);
                AddBox(bodyTool, doorTile.X + 0.12f, doorTile.X + 0.88f, doorTile.Y + 0.92f, doorTile.Y + 1.04f, wallBase, wallBase + 0.05f, trimColor, trimColor.Darkened(0.12f));
            }
            else if (entry.OutwardDirection == Vec3i.North)
            {
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + 1f, doorTile.Y, doorTile.Y + wallThickness, doorHeight, wallTop, wallColor, wallShadow);
                AddBox(bodyTool, doorTile.X + 0.12f, doorTile.X + 0.88f, doorTile.Y - 0.04f, doorTile.Y + 0.08f, wallBase, wallBase + 0.05f, trimColor, trimColor.Darkened(0.12f));
            }
            else if (entry.OutwardDirection == Vec3i.East)
            {
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y, doorTile.Y + 1f, doorHeight, wallTop, wallColor, wallShadow);
                AddBox(bodyTool, doorTile.X + 0.92f, doorTile.X + 1.04f, doorTile.Y + 0.12f, doorTile.Y + 0.88f, wallBase, wallBase + 0.05f, trimColor, trimColor.Darkened(0.12f));
            }
            else if (entry.OutwardDirection == Vec3i.West)
            {
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y, doorTile.Y + 1f, doorHeight, wallTop, wallColor, wallShadow);
                AddBox(bodyTool, doorTile.X - 0.04f, doorTile.X + 0.08f, doorTile.Y + 0.12f, doorTile.Y + 0.88f, wallBase, wallBase + 0.05f, trimColor, trimColor.Darkened(0.12f));
            }
        }

        AddBox(roofTool, bounds.MinX - 0.08f, bounds.MaxX + 1.08f, bounds.MinY - 0.08f, bounds.MaxY + 1.08f, 0.70f, 0.90f, roofColor, roofColor.Darkened(0.20f));
        var roofCenterX = (bounds.MinX + bounds.MaxX + 1f) * 0.5f;
        var roofCenterY = (bounds.MinY + bounds.MaxY + 1f) * 0.5f;
        if (entry.OutwardDirection == Vec3i.East || entry.OutwardDirection == Vec3i.West)
        {
            AddBox(roofTool, roofCenterX - 0.10f, roofCenterX + 0.10f, bounds.MinY - 0.12f, bounds.MaxY + 1.12f, 0.90f, 1.05f, roofHighlight, roofHighlight.Darkened(0.20f));
        }
        else
        {
            AddBox(roofTool, bounds.MinX - 0.12f, bounds.MaxX + 1.12f, roofCenterY - 0.10f, roofCenterY + 0.10f, 0.90f, 1.05f, roofHighlight, roofHighlight.Darkened(0.20f));
        }

        return new StructureMeshParts(bodyTool.Commit(), roofTool.Commit(), profile.HideRoofOnHover);
    }

    private static (Color Body, Color Roof, Color Accent) ResolveWorkshopColors(string? palette)
    {
        return palette?.Trim().ToLowerInvariant() switch
        {
            "carpenter" => (new Color(0.50f, 0.34f, 0.18f), new Color(0.63f, 0.46f, 0.26f), new Color(0.88f, 0.76f, 0.53f)),
            "kitchen" => (new Color(0.45f, 0.28f, 0.24f), new Color(0.62f, 0.36f, 0.28f), new Color(0.93f, 0.78f, 0.63f)),
            "still" => (new Color(0.28f, 0.34f, 0.40f), new Color(0.41f, 0.48f, 0.56f), new Color(0.78f, 0.86f, 0.92f)),
            "smelter" => (new Color(0.34f, 0.34f, 0.38f), new Color(0.46f, 0.46f, 0.52f), new Color(0.88f, 0.54f, 0.18f)),
            _ => (new Color(0.42f, 0.36f, 0.30f), new Color(0.58f, 0.50f, 0.44f), new Color(0.86f, 0.82f, 0.74f)),
        };
    }

    private static (Color Floor, Color FloorSeam, Color Wall, Color Trim, Color Roof, Color RoofHighlight, Color Door) ResolveHutColors(string? palette)
    {
        return palette?.Trim().ToLowerInvariant() switch
        {
            "cool_wood" => (
                new Color(0.50f, 0.40f, 0.30f),
                new Color(0.27f, 0.22f, 0.18f),
                new Color(0.64f, 0.55f, 0.44f),
                new Color(0.30f, 0.24f, 0.19f),
                new Color(0.32f, 0.27f, 0.23f),
                new Color(0.45f, 0.38f, 0.31f),
                new Color(0.20f, 0.16f, 0.12f)),
            _ => (
                new Color(0.60f, 0.42f, 0.24f),
                new Color(0.32f, 0.20f, 0.12f),
                new Color(0.74f, 0.56f, 0.35f),
                new Color(0.38f, 0.24f, 0.13f),
                new Color(0.40f, 0.25f, 0.15f),
                new Color(0.52f, 0.34f, 0.20f),
                new Color(0.26f, 0.15f, 0.08f)),
        };
    }

    private static void AddBox(SurfaceTool surfaceTool, float minX, float maxX, float minZ, float maxZ, float baseY, float topY, Color topColor, Color sideColor)
    {
        var a = new Vector3(minX, topY, minZ);
        var b = new Vector3(maxX, topY, minZ);
        var c = new Vector3(maxX, topY, maxZ);
        var d = new Vector3(minX, topY, maxZ);

        var ba = new Vector3(minX, baseY, minZ);
        var bb = new Vector3(maxX, baseY, minZ);
        var bc = new Vector3(maxX, baseY, maxZ);
        var bd = new Vector3(minX, baseY, maxZ);

        AddQuad(surfaceTool, a, b, c, d, topColor);
        AddQuad(surfaceTool, ba, bb, b, a, sideColor);
        AddQuad(surfaceTool, bb, bc, c, b, sideColor);
        AddQuad(surfaceTool, bc, bd, d, c, sideColor);
        AddQuad(surfaceTool, bd, ba, a, d, sideColor);
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
}
