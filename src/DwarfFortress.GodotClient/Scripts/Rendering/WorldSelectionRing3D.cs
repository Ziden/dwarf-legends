using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

public sealed partial class WorldSelectionRing3D : Node3D
{
    private const float RingInset = 0.12f;
    private const float RingWidth = 0.09f;
    private const float RingThickness = 0.02f;
    private const float SurfaceOffset = 0.014f;
    private MeshInstance3D? _meshInstance;
    private StandardMaterial3D? _material;
    private bool _isActive;

    public override void _Ready()
    {
        EnsureMaterial();
        SetActive(false);
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        if (_meshInstance is not null)
            _meshInstance.Visible = active && _meshInstance.Mesh is not null;
    }

    public void Sync(WorldQuerySystem? query, InputController? input, WorldMap? map, int currentZ, Rect2I visibleTileBounds)
    {
        if (!_isActive || query is null || input is null || map is null)
        {
            ClearMesh();
            return;
        }

        var plates = new List<WorldOverlayMesher.OverlayPlate>();

        if (input.SelectedDwarfId is int dwarfId)
        {
            var dwarf = query.GetDwarfView(dwarfId);
            if (dwarf is not null && dwarf.Position.Z == currentZ)
                AddRing(plates, map, dwarf.Position, visibleTileBounds, new Color(1f, 0.94f, 0.18f, 0.42f));
        }

        if (input.SelectedCreatureId is int creatureId)
        {
            var creature = query.GetCreatureView(creatureId);
            if (creature is not null && creature.Position.Z == currentZ)
                AddRing(plates, map, creature.Position, visibleTileBounds, new Color(1f, 0.52f, 0.20f, 0.40f));
        }

        if (input.SelectedItemId is int itemId)
        {
            var item = query.GetItemView(itemId);
            if (item is not null && item.Position.Z == currentZ)
                AddRing(plates, map, item.Position, visibleTileBounds, new Color(0.92f, 0.74f, 1f, 0.38f), inset: 0.18f, ringWidth: 0.07f);
        }

        SyncMesh(WorldOverlayMesher.Build(plates));
    }

    private void AddRing(List<WorldOverlayMesher.OverlayPlate> plates, WorldMap map, Vec3i position, Rect2I visibleTileBounds, Color color, float inset = RingInset, float ringWidth = RingWidth)
    {
        if (!Contains(visibleTileBounds, position.X, position.Y))
            return;

        var baseY = ResolveBaseY(map, position);
        var outerMinX = position.X + inset;
        var outerMaxX = position.X + 1f - inset;
        var outerMinY = position.Y + inset;
        var outerMaxY = position.Y + 1f - inset;
        var innerMinX = outerMinX + ringWidth;
        var innerMaxX = outerMaxX - ringWidth;
        var innerMinY = outerMinY + ringWidth;
        var innerMaxY = outerMaxY - ringWidth;

        if (innerMinX >= innerMaxX || innerMinY >= innerMaxY)
        {
            AddPlate(plates, outerMinX, outerMaxX, outerMinY, outerMaxY, baseY, color);
            return;
        }

        AddPlate(plates, outerMinX, outerMaxX, outerMinY, innerMinY, baseY, color);
        AddPlate(plates, outerMinX, outerMaxX, innerMaxY, outerMaxY, baseY, color);
        AddPlate(plates, outerMinX, innerMinX, innerMinY, innerMaxY, baseY, color);
        AddPlate(plates, innerMaxX, outerMaxX, innerMinY, innerMaxY, baseY, color);
    }

    private static void AddPlate(List<WorldOverlayMesher.OverlayPlate> plates, float minX, float maxX, float minY, float maxY, float baseY, Color color)
    {
        if (maxX <= minX || maxY <= minY)
            return;

        plates.Add(new WorldOverlayMesher.OverlayPlate(
            minX,
            maxX,
            minY,
            maxY,
            baseY,
            baseY + RingThickness,
            color,
            color.Darkened(0.18f)));
    }

    private float ResolveBaseY(WorldMap map, Vec3i position)
    {
        if (!map.IsInBounds(position))
            return WorldTileHeightResolver3D.ResolveSliceY(position.Z, SurfaceOffset);

        var tile = map.GetTile(position);
        if (tile.TileDefId == TileDefIds.Empty)
            return WorldTileHeightResolver3D.ResolveSliceY(position.Z, SurfaceOffset);

        return WorldTileHeightResolver3D.ResolveSurfaceY(position.Z, tile, SurfaceOffset);
    }

    private void SyncMesh(ArrayMesh? mesh)
    {
        if (mesh is null)
        {
            ClearMesh();
            return;
        }

        EnsureMaterial();
        var meshInstance = _meshInstance ?? CreateMeshInstance();
        ReplaceOwnedMesh(meshInstance, mesh);
        meshInstance.MaterialOverride = _material;
        meshInstance.Visible = _isActive;
        _meshInstance = meshInstance;
    }

    private MeshInstance3D CreateMeshInstance()
    {
        var meshInstance = new MeshInstance3D
        {
            Name = "SelectionRing3D",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        AddChild(meshInstance);
        return meshInstance;
    }

    private void EnsureMaterial()
    {
        if (_material is not null)
            return;

        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private void ClearMesh()
    {
        if (_meshInstance is null)
            return;

        ReplaceOwnedMesh(_meshInstance, null);
        _meshInstance.Visible = false;
    }

    private static void ReplaceOwnedMesh(MeshInstance3D meshInstance, Mesh? nextMesh)
    {
        var previousMesh = meshInstance.Mesh;
        meshInstance.Mesh = nextMesh;
        if (!ReferenceEquals(previousMesh, nextMesh))
            DisposeResource(previousMesh);
    }

    private static void DisposeResource(IDisposable? resource)
        => resource?.Dispose();

    private static bool Contains(Rect2I bounds, int x, int y)
    {
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0)
            return false;

        return x >= bounds.Position.X
            && x < bounds.Position.X + bounds.Size.X
            && y >= bounds.Position.Y
            && y < bounds.Position.Y + bounds.Size.Y;
    }

}

