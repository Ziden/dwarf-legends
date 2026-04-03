using System;
using System.Linq;
using DwarfFortress.GameLogic.Data.Defs;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public sealed class WorldWorkshopMesher
{
    public ArrayMesh? BuildWorkshopMesh(BuildingDef definition, string buildingDefId)
    {
        if (definition.Footprint.Count == 0)
            return null;

        var minX = definition.Footprint.Min(tile => tile.Offset.X);
        var maxX = definition.Footprint.Max(tile => tile.Offset.X) + 1;
        var minY = definition.Footprint.Min(tile => tile.Offset.Y);
        var maxY = definition.Footprint.Max(tile => tile.Offset.Y) + 1;

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var (bodyColor, roofColor, accentColor) = ResolveColors(buildingDefId);
        AddBox(surfaceTool, minX, maxX, minY, maxY, 0.02f, 0.62f, bodyColor, bodyColor.Darkened(0.22f));
        AddBox(surfaceTool, minX + 0.08f, maxX - 0.08f, minY + 0.08f, maxY - 0.08f, 0.62f, 0.82f, roofColor, roofColor.Darkened(0.18f));

        var centerX = ((minX + maxX) * 0.5f);
        var frontY = minY + 0.12f;
        AddBox(surfaceTool, centerX - 0.12f, centerX + 0.12f, frontY, frontY + 0.10f, 0.10f, 0.42f, accentColor, accentColor.Darkened(0.12f));

        return surfaceTool.Commit();
    }

    private static (Color Body, Color Roof, Color Accent) ResolveColors(string buildingDefId)
    {
        return buildingDefId switch
        {
            BuildingDefIds.CarpenterWorkshop => (new Color(0.50f, 0.34f, 0.18f), new Color(0.63f, 0.46f, 0.26f), new Color(0.88f, 0.76f, 0.53f)),
            BuildingDefIds.Kitchen => (new Color(0.45f, 0.28f, 0.24f), new Color(0.62f, 0.36f, 0.28f), new Color(0.93f, 0.78f, 0.63f)),
            BuildingDefIds.Still => (new Color(0.28f, 0.34f, 0.40f), new Color(0.41f, 0.48f, 0.56f), new Color(0.78f, 0.86f, 0.92f)),
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