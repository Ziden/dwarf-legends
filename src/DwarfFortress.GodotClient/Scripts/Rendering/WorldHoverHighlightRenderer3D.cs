using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using DwarfFortress.GodotClient.Presentation;
using Godot;

using WorldTileData = DwarfFortress.GameLogic.World.TileData;

namespace DwarfFortress.GodotClient.Rendering;

internal sealed partial class WorldHoverHighlightRenderer3D : Node3D
{
    private const float SurfaceOffset = 0.016f;
    private const float RingThickness = 0.028f;
    private const float DefaultRingInset = 0.12f;
    private const float DefaultRingWidth = 0.08f;
    private const float BuildingRingInset = 0.05f;
    private const float BuildingRingWidth = 0.11f;
    private const float BillboardLift = 0.035f;
    private const float BillboardPulseScale = 0.045f;
    private const float BillboardBaseScale = 1.06f;
    private const int HoverRingRenderPriority = 4;
    private const int HoverBillboardRenderPriority = 5;
    private const int HoverBillboardOverlayRenderPriority = 6;
    private const float AlphaScissorThreshold = 0.5f;

    private MeshInstance3D? _ringMesh;
    private StandardMaterial3D? _ringMaterial;
    private Node3D? _billboardRoot;
    private MeshInstance3D? _billboardMesh;
    private MeshInstance3D? _billboardOverlayMesh;
    private StandardMaterial3D? _billboardMaterial;
    private StandardMaterial3D? _billboardOverlayMaterial;
    private bool _isActive;
    private string _debugTargetKey = HoverWorldTarget.None.DebugKey;

    public string DebugTargetKey => _debugTargetKey;

    public override void _Ready()
    {
        SetActive(false);
    }

    public void Reset()
    {
        ClearRingMesh();
        ClearBillboard();
        _debugTargetKey = HoverWorldTarget.None.DebugKey;
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        if (_ringMesh is not null)
            _ringMesh.Visible = active && _ringMesh.Mesh is not null;

        if (_billboardRoot is not null)
            _billboardRoot.Visible = active && _billboardMesh?.Visible == true;
    }

    public void Sync(
        WorldMap? map,
        WorldQuerySystem? query,
        DataManager? data,
        HoverWorldTarget hoverTarget,
        WorldActorPresentation3D? actorPresentation,
        VegetationInstanceRenderer? vegetationRenderer,
        int currentZ,
        Rect2I visibleTileBounds,
        double presentationTimeSeconds)
    {
        _debugTargetKey = hoverTarget.DebugKey;

        if (!_isActive
            || map is null
            || hoverTarget.Kind is HoverWorldTargetKind.None or HoverWorldTargetKind.RawTile
            || hoverTarget.TilePosition.Z != currentZ
            || !TileBoundsContains(visibleTileBounds, hoverTarget.TilePosition.X, hoverTarget.TilePosition.Y))
        {
            ClearRingMesh();
            ClearBillboard();
            return;
        }

        var pulse = 0.5f + (0.5f * Mathf.Sin((float)presentationTimeSeconds * 5.2f));
        var billboardScale = BillboardBaseScale + (pulse * BillboardPulseScale);
        var billboardLift = BillboardLift + (pulse * 0.014f);
        var ringPlates = new List<WorldOverlayMesher.OverlayPlate>();

        switch (hoverTarget.Kind)
        {
            case HoverWorldTargetKind.Dwarf:
                if (hoverTarget.TargetId.HasValue
                    && actorPresentation?.TryGetHoverPresentation(hoverTarget.TargetId.Value, out var dwarfBillboard) == true)
                {
                    AddRing(ringPlates, map, dwarfBillboard.TilePosition, visibleTileBounds, new Color(1f, 0.90f, 0.38f, 0.42f));
                    SyncBillboard(
                        dwarfBillboard.Texture,
                        overlayTexture: null,
                        dwarfBillboard.WorldPosition,
                        dwarfBillboard.Size,
                        new Color(1f, 0.98f, 0.88f, 0.94f),
                        billboardScale,
                        billboardLift,
                        useAlphaScissor: false);
                }
                else
                {
                    ClearRingMesh();
                    ClearBillboard();
                    return;
                }
                break;

            case HoverWorldTargetKind.Creature:
                if (hoverTarget.TargetId.HasValue
                    && actorPresentation?.TryGetHoverPresentation(hoverTarget.TargetId.Value, out var creatureBillboard) == true)
                {
                    AddRing(ringPlates, map, creatureBillboard.TilePosition, visibleTileBounds, new Color(1f, 0.58f, 0.24f, 0.42f));
                    SyncBillboard(
                        creatureBillboard.Texture,
                        overlayTexture: null,
                        creatureBillboard.WorldPosition,
                        creatureBillboard.Size,
                        new Color(1f, 0.94f, 0.88f, 0.94f),
                        billboardScale,
                        billboardLift,
                        useAlphaScissor: false);
                }
                else
                {
                    ClearRingMesh();
                    ClearBillboard();
                    return;
                }
                break;

            case HoverWorldTargetKind.Item:
                if (hoverTarget.TargetId.HasValue
                    && actorPresentation?.TryGetHoverPresentation(hoverTarget.TargetId.Value, out var itemBillboard) == true)
                {
                    AddRing(ringPlates, map, itemBillboard.TilePosition, visibleTileBounds, new Color(0.84f, 0.72f, 1f, 0.40f), inset: 0.18f, ringWidth: 0.07f);
                    SyncBillboard(
                        itemBillboard.Texture,
                        overlayTexture: null,
                        itemBillboard.WorldPosition,
                        itemBillboard.Size,
                        new Color(0.98f, 0.94f, 1f, 0.92f),
                        billboardScale,
                        billboardLift,
                        useAlphaScissor: false);
                }
                else
                {
                    ClearRingMesh();
                    ClearBillboard();
                    return;
                }
                break;

            case HoverWorldTargetKind.Tree:
                if (vegetationRenderer?.TryGetHoverPresentation(hoverTarget.TilePosition, VegetationInstanceKind.Tree, out var treeBillboard) == true)
                {
                    AddRing(ringPlates, map, treeBillboard.TilePosition, visibleTileBounds, new Color(0.46f, 0.92f, 0.56f, 0.38f));
                    SyncBillboard(
                        treeBillboard.Texture,
                        treeBillboard.OverlayTexture,
                        treeBillboard.FootPosition,
                        treeBillboard.Size,
                        new Color(0.92f, 1f, 0.93f, 0.94f),
                        billboardScale,
                        billboardLift,
                        useAlphaScissor: true);
                }
                else
                {
                    ClearRingMesh();
                    ClearBillboard();
                    return;
                }
                break;

            case HoverWorldTargetKind.Plant:
                if (vegetationRenderer?.TryGetHoverPresentation(hoverTarget.TilePosition, VegetationInstanceKind.Plant, out var plantBillboard) == true)
                {
                    AddRing(ringPlates, map, plantBillboard.TilePosition, visibleTileBounds, new Color(0.72f, 1f, 0.46f, 0.36f), inset: 0.16f, ringWidth: 0.07f);
                    SyncBillboard(
                        plantBillboard.Texture,
                        plantBillboard.OverlayTexture,
                        plantBillboard.FootPosition,
                        plantBillboard.Size,
                        new Color(0.96f, 1f, 0.92f, 0.94f),
                        billboardScale,
                        billboardLift,
                        useAlphaScissor: true);
                }
                else
                {
                    ClearRingMesh();
                    ClearBillboard();
                    return;
                }
                break;

            case HoverWorldTargetKind.Building:
                if (hoverTarget.TargetId.HasValue && query?.GetBuildingView(hoverTarget.TargetId.Value) is BuildingView building)
                {
                    AddBuildingFootprintRing(
                        ringPlates,
                        map,
                        data,
                        building.Origin,
                        building.BuildingDefId,
                        building.Rotation,
                        currentZ,
                        visibleTileBounds,
                        new Color(0.42f, 0.92f, 1f, 0.38f));
                    ClearBillboard();
                }
                else
                {
                    ClearRingMesh();
                    ClearBillboard();
                    return;
                }
                break;

            default:
                ClearRingMesh();
                ClearBillboard();
                return;
        }

        SyncRingMesh(WorldOverlayMesher.Build(ringPlates));
    }

    public bool HasDebugActiveHighlight()
        => HasDebugRing() || HasDebugBillboard();

    public bool HasDebugRing()
        => _ringMesh?.Visible == true && _ringMesh.Mesh is not null;

    public bool HasDebugBillboard()
        => _billboardRoot?.Visible == true && _billboardMesh?.Visible == true;

    public int GetDebugBillboardCount()
        => HasDebugBillboard() ? 1 : 0;

    public bool TryGetDebugBillboardTexture(out Texture2D? texture)
    {
        texture = _billboardMaterial?.AlbedoTexture;
        return texture is not null;
    }

    public bool TryGetDebugBillboardWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = default;
        if (_billboardRoot is null || !_billboardRoot.Visible)
            return false;

        worldPosition = _billboardRoot.GlobalTransform.Origin;
        return true;
    }

    private void AddBuildingFootprintRing(
        List<WorldOverlayMesher.OverlayPlate> plates,
        WorldMap map,
        DataManager? data,
        Vec3i origin,
        string buildingDefId,
        BuildingRotation rotation,
        int currentZ,
        Rect2I visibleTileBounds,
        Color color)
    {
        var definition = data?.Buildings.GetOrNull(buildingDefId);
        if (definition is null || definition.Footprint.Count == 0)
        {
            AddRing(plates, map, origin, visibleTileBounds, color, BuildingRingInset, BuildingRingWidth);
            return;
        }

        foreach (var position in BuildingPlacementGeometry.EnumerateWorldFootprint(definition, origin, rotation))
        {
            if (position.Z != currentZ)
                continue;

            AddRing(plates, map, position, visibleTileBounds, color, BuildingRingInset, BuildingRingWidth);
        }
    }

    private void AddRing(
        List<WorldOverlayMesher.OverlayPlate> plates,
        WorldMap map,
        Vec3i position,
        Rect2I visibleTileBounds,
        Color color,
        float inset = DefaultRingInset,
        float ringWidth = DefaultRingWidth)
    {
        if (!TileBoundsContains(visibleTileBounds, position.X, position.Y))
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

    private static void AddPlate(
        List<WorldOverlayMesher.OverlayPlate> plates,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float baseY,
        Color color)
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
            color.Darkened(0.20f)));
    }

    private void SyncBillboard(
        Texture2D texture,
        Texture2D? overlayTexture,
        Vector3 footPosition,
        Vector2 size,
        Color tint,
        float scale,
        float lift,
        bool useAlphaScissor)
    {
        EnsureBillboardNodes();
        var scaledSize = size * scale;
        var mainMesh = _billboardMesh!.Mesh as QuadMesh ?? new QuadMesh();
        mainMesh.Size = scaledSize;
        _billboardMesh.Mesh = mainMesh;
        _billboardMesh.Position = new Vector3(0f, scaledSize.Y * 0.5f, 0f);
        _billboardMesh.MaterialOverride = ConfigureBillboardMaterial(
            _billboardMaterial,
            texture,
            tint,
            HoverBillboardRenderPriority,
            useAlphaScissor);

        if (overlayTexture is not null)
        {
            var overlayMesh = _billboardOverlayMesh!.Mesh as QuadMesh ?? new QuadMesh();
            overlayMesh.Size = scaledSize;
            _billboardOverlayMesh.Mesh = overlayMesh;
            _billboardOverlayMesh.Position = new Vector3(0f, scaledSize.Y * 0.5f, 0f);
            _billboardOverlayMesh.MaterialOverride = ConfigureBillboardMaterial(
                _billboardOverlayMaterial,
                overlayTexture,
                tint,
                HoverBillboardOverlayRenderPriority,
                useAlphaScissor);
            _billboardOverlayMesh.Visible = _isActive;
        }
        else if (_billboardOverlayMesh is not null)
        {
            _billboardOverlayMesh.Visible = false;
        }

        _billboardRoot!.Position = footPosition + new Vector3(0f, lift, 0f);
        _billboardMesh.Visible = _isActive;
        _billboardRoot.Visible = _isActive;
    }

    private StandardMaterial3D ConfigureBillboardMaterial(
        StandardMaterial3D? material,
        Texture2D texture,
        Color tint,
        int renderPriority,
        bool useAlphaScissor)
    {
        material ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };

        material.Transparency = useAlphaScissor
            ? BaseMaterial3D.TransparencyEnum.AlphaScissor
            : BaseMaterial3D.TransparencyEnum.Alpha;
        material.AlphaScissorThreshold = useAlphaScissor ? AlphaScissorThreshold : 0f;
        material.AlbedoTexture = texture;
        material.AlbedoColor = tint;
        material.RenderPriority = renderPriority;

        if (renderPriority == HoverBillboardRenderPriority)
            _billboardMaterial = material;
        else
            _billboardOverlayMaterial = material;

        return material;
    }

    private void EnsureBillboardNodes()
    {
        if (_billboardRoot is not null && _billboardMesh is not null && _billboardOverlayMesh is not null)
            return;

        _billboardRoot ??= new Node3D
        {
            Name = "HoverBillboardRoot",
            Visible = false,
        };
        if (_billboardRoot.GetParent() is null)
            AddChild(_billboardRoot);

        _billboardMesh ??= CreateBillboardMesh("HoverBillboard");
        if (_billboardMesh.GetParent() is null)
            _billboardRoot.AddChild(_billboardMesh);

        _billboardOverlayMesh ??= CreateBillboardMesh("HoverBillboardOverlay");
        if (_billboardOverlayMesh.GetParent() is null)
            _billboardRoot.AddChild(_billboardOverlayMesh);
    }

    private static MeshInstance3D CreateBillboardMesh(string name)
    {
        return new MeshInstance3D
        {
            Name = name,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
    }

    private void ClearBillboard()
    {
        if (_billboardMesh is not null)
            _billboardMesh.Visible = false;

        if (_billboardOverlayMesh is not null)
            _billboardOverlayMesh.Visible = false;

        if (_billboardRoot is not null)
            _billboardRoot.Visible = false;
    }

    private void SyncRingMesh(ArrayMesh? mesh)
    {
        if (mesh is null)
        {
            ClearRingMesh();
            return;
        }

        EnsureRingMaterial();
        _ringMesh ??= CreateRingMeshInstance();
        ReplaceOwnedMesh(_ringMesh, mesh);
        _ringMesh.MaterialOverride = _ringMaterial;
        _ringMesh.Visible = _isActive;
    }

    private MeshInstance3D CreateRingMeshInstance()
    {
        var meshInstance = new MeshInstance3D
        {
            Name = "HoverRing",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        AddChild(meshInstance);
        return meshInstance;
    }

    private void EnsureRingMaterial()
    {
        if (_ringMaterial is not null)
            return;

        _ringMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
            RenderPriority = HoverRingRenderPriority,
        };
    }

    private void ClearRingMesh()
    {
        if (_ringMesh is null)
            return;

        ReplaceOwnedMesh(_ringMesh, null);
        _ringMesh.Visible = false;
    }

    private float ResolveBaseY(WorldMap map, Vec3i position)
    {
        if (!map.IsInBounds(position))
            return WorldTileHeightResolver3D.ResolveSliceY(position.Z, SurfaceOffset);

        var tile = map.GetTile(position);
        if (tile.TileDefId == TileDefIds.Empty)
            return WorldTileHeightResolver3D.ResolveSliceY(position.Z, SurfaceOffset);

        return ResolveBaseY(position.Z, tile);
    }

    private static float ResolveBaseY(int currentZ, WorldTileData tile)
        => WorldTileHeightResolver3D.ResolveSurfaceY(currentZ, tile, SurfaceOffset);

    private static bool TileBoundsContains(Rect2I visibleTileBounds, int x, int y)
    {
        if (visibleTileBounds.Size.X <= 0 || visibleTileBounds.Size.Y <= 0)
            return false;

        return x >= visibleTileBounds.Position.X
            && x < visibleTileBounds.Position.X + visibleTileBounds.Size.X
            && y >= visibleTileBounds.Position.Y
            && y < visibleTileBounds.Position.Y + visibleTileBounds.Size.Y;
    }

    private static void ReplaceOwnedMesh(MeshInstance3D meshInstance, Mesh? nextMesh)
    {
        var previousMesh = meshInstance.Mesh;
        meshInstance.Mesh = nextMesh;
        if (!ReferenceEquals(previousMesh, nextMesh))
            previousMesh?.Dispose();
    }
}
