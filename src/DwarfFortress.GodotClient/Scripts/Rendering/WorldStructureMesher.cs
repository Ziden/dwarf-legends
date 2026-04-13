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
        if (definition.Footprint.Count == 0)
            return new StructureMeshParts(null, null, false);

        if (string.Equals(definition.StructureVisualId, StructureVisualIds.HouseWood3x3, System.StringComparison.Ordinal))
            return BuildHouseMeshes(definition, building.Rotation);

        return BuildWorkshopMeshes(definition, building.BuildingDefId, building.Rotation);
    }

    private static StructureMeshParts BuildWorkshopMeshes(BuildingDef definition, string buildingDefId, BuildingRotation rotation)
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

        var (bodyColor, roofColor, accentColor) = ResolveWorkshopColors(buildingDefId);
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

    private static StructureMeshParts BuildHouseMeshes(BuildingDef definition, BuildingRotation rotation)
    {
        var bounds = BuildingPlacementGeometry.GetRotatedBounds(definition, rotation);
        var bodyTool = new SurfaceTool();
        bodyTool.Begin(Mesh.PrimitiveType.Triangles);

        var roofTool = new SurfaceTool();
        roofTool.Begin(Mesh.PrimitiveType.Triangles);

        var floorColor = new Color(0.58f, 0.40f, 0.22f);
        var wallColor = new Color(0.70f, 0.52f, 0.32f);
        var wallShadow = wallColor.Darkened(0.20f);
        var roofColor = new Color(0.42f, 0.28f, 0.18f);
        var doorColor = new Color(0.32f, 0.20f, 0.11f);

        AddBox(bodyTool, bounds.MinX, bounds.MaxX + 1, bounds.MinY, bounds.MaxY + 1, 0.02f, 0.08f, floorColor, floorColor.Darkened(0.16f));

        var wallThickness = 0.12f;
        var wallBase = 0.08f;
        var wallTop = 0.74f;
        var doorHeight = 0.52f;
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

        if (entry != default)
        {
            if (entry.OutwardDirection == Vec3i.South)
            {
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + 1f, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, doorHeight, wallTop, wallColor, wallShadow);
            }
            else if (entry.OutwardDirection == Vec3i.North)
            {
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + 1f, doorTile.Y, doorTile.Y + wallThickness, doorHeight, wallTop, wallColor, wallShadow);
            }
            else if (entry.OutwardDirection == Vec3i.East)
            {
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X + 1f - wallThickness, doorTile.X + 1f, doorTile.Y, doorTile.Y + 1f, doorHeight, wallTop, wallColor, wallShadow);
            }
            else if (entry.OutwardDirection == Vec3i.West)
            {
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y, doorTile.Y + wallThickness, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y + 1f - wallThickness, doorTile.Y + 1f, wallBase, doorHeight, doorColor, doorColor.Darkened(0.15f));
                AddBox(bodyTool, doorTile.X, doorTile.X + wallThickness, doorTile.Y, doorTile.Y + 1f, doorHeight, wallTop, wallColor, wallShadow);
            }
        }

        AddBox(roofTool, bounds.MinX - 0.02f, bounds.MaxX + 1.02f, bounds.MinY - 0.02f, bounds.MaxY + 1.02f, 0.74f, 0.98f, roofColor, roofColor.Darkened(0.18f));

        return new StructureMeshParts(bodyTool.Commit(), roofTool.Commit(), HideRoofOnHover: true);
    }

    private static (Color Body, Color Roof, Color Accent) ResolveWorkshopColors(string buildingDefId)
    {
        return buildingDefId switch
        {
            BuildingDefIds.CarpenterWorkshop => (new Color(0.50f, 0.34f, 0.18f), new Color(0.63f, 0.46f, 0.26f), new Color(0.88f, 0.76f, 0.53f)),
            BuildingDefIds.Kitchen => (new Color(0.45f, 0.28f, 0.24f), new Color(0.62f, 0.36f, 0.28f), new Color(0.93f, 0.78f, 0.63f)),
            BuildingDefIds.Still => (new Color(0.28f, 0.34f, 0.40f), new Color(0.41f, 0.48f, 0.56f), new Color(0.78f, 0.86f, 0.92f)),
            BuildingDefIds.Smelter => (new Color(0.34f, 0.34f, 0.38f), new Color(0.46f, 0.46f, 0.52f), new Color(0.88f, 0.54f, 0.18f)),
            _ => (new Color(0.42f, 0.36f, 0.30f), new Color(0.58f, 0.50f, 0.44f), new Color(0.86f, 0.82f, 0.74f)),
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
