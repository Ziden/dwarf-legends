using System.Collections.Generic;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class WorldWaterEffectMesher
{
    public readonly record struct WaterQuad(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Color Color);

    public static ArrayMesh? Build(IReadOnlyList<WaterQuad> quads)
    {
        if (quads.Count == 0)
            return null;

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var quad in quads)
            AddQuad(surfaceTool, quad);

        return surfaceTool.Commit();
    }

    private static void AddQuad(SurfaceTool surfaceTool, WaterQuad quad)
    {
        surfaceTool.SetColor(quad.Color);
        surfaceTool.AddVertex(quad.A);
        surfaceTool.SetColor(quad.Color);
        surfaceTool.AddVertex(quad.B);
        surfaceTool.SetColor(quad.Color);
        surfaceTool.AddVertex(quad.C);

        surfaceTool.SetColor(quad.Color);
        surfaceTool.AddVertex(quad.A);
        surfaceTool.SetColor(quad.Color);
        surfaceTool.AddVertex(quad.C);
        surfaceTool.SetColor(quad.Color);
        surfaceTool.AddVertex(quad.D);
    }
}