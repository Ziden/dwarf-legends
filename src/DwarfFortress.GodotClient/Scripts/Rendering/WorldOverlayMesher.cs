using System.Collections.Generic;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class WorldOverlayMesher
{
    public readonly record struct OverlayPlate(
        float MinX,
        float MaxX,
        float MinZ,
        float MaxZ,
        float BaseY,
        float TopY,
        Color TopColor,
        Color SideColor);

    public static ArrayMesh? Build(IReadOnlyList<OverlayPlate> plates)
    {
        if (plates.Count == 0)
            return null;

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var plate in plates)
            AddPlate(surfaceTool, plate);

        return surfaceTool.Commit();
    }

    private static void AddPlate(SurfaceTool surfaceTool, OverlayPlate plate)
    {
        var a = new Vector3(plate.MinX, plate.TopY, plate.MinZ);
        var b = new Vector3(plate.MaxX, plate.TopY, plate.MinZ);
        var c = new Vector3(plate.MaxX, plate.TopY, plate.MaxZ);
        var d = new Vector3(plate.MinX, plate.TopY, plate.MaxZ);

        var ba = new Vector3(plate.MinX, plate.BaseY, plate.MinZ);
        var bb = new Vector3(plate.MaxX, plate.BaseY, plate.MinZ);
        var bc = new Vector3(plate.MaxX, plate.BaseY, plate.MaxZ);
        var bd = new Vector3(plate.MinX, plate.BaseY, plate.MaxZ);

        AddQuad(surfaceTool, a, b, c, d, plate.TopColor);
        AddQuad(surfaceTool, ba, bb, b, a, plate.SideColor);
        AddQuad(surfaceTool, bb, bc, c, b, plate.SideColor);
        AddQuad(surfaceTool, bc, bd, d, c, plate.SideColor);
        AddQuad(surfaceTool, bd, ba, a, d, plate.SideColor);
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