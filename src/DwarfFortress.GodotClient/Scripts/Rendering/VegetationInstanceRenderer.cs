using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

internal enum VegetationInstanceKind : byte
{
    Tree = 0,
    Plant = 1,
}

internal readonly record struct VegetationVisualInstance(
    Vec3i TilePosition,
    VegetationInstanceKind Kind,
    Texture2D Texture,
    Texture2D? OverlayTexture,
    Vector3 FootPosition,
    Vector2 Size);

internal readonly record struct VegetationHoverPresentation(
    Vec3i TilePosition,
    VegetationInstanceKind Kind,
    Texture2D Texture,
    Texture2D? OverlayTexture,
    Vector3 FootPosition,
    Vector2 Size);

internal partial class VegetationInstanceRenderer : Node3D
{
    private const int BillboardRenderPriority = 1;
    private const int BillboardOverlayRenderPriority = 2;
    private const float AlphaHitThreshold = 0.01f;
    private const float AlphaScissorThreshold = 0.5f;
    private const BaseMaterial3D.TransparencyEnum VegetationTransparencyMode = BaseMaterial3D.TransparencyEnum.AlphaScissor;
    private static readonly Dictionary<Texture2D, Image> TextureImageCache = new();

    private readonly Dictionary<VegetationBatchKey, VegetationBatchState> _batches = new();
    private readonly List<VegetationVisualState> _visibleStates = new();
    private readonly Dictionary<Vec3i, VegetationVisualState> _treeStatesByTile = new();
    private readonly Dictionary<Vec3i, VegetationVisualState> _plantStatesByTile = new();

    private bool _isActive;
    public int TreeCount { get; private set; }

    public int PlantCount { get; private set; }

    public void Reset()
    {
        foreach (var batch in _batches.Values)
            ReleaseBatch(batch);

        _batches.Clear();
        _visibleStates.Clear();
        _treeStatesByTile.Clear();
        _plantStatesByTile.Clear();
        TreeCount = 0;
        PlantCount = 0;
        TerrainRenderStats.RecordVegetationFrame(0, 0);
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        foreach (var batch in _batches.Values)
            ApplyBatchVisibility(batch);
    }

    public void SyncVisibleInstances(IReadOnlyList<VegetationVisualInstance> instances)
    {
        _visibleStates.Clear();
        _treeStatesByTile.Clear();
        _plantStatesByTile.Clear();
        TreeCount = 0;
        PlantCount = 0;

        foreach (var instance in instances)
        {
            var state = new VegetationVisualState(instance);
            _visibleStates.Add(state);
            if (instance.Kind == VegetationInstanceKind.Tree)
            {
                TreeCount++;
                _treeStatesByTile[instance.TilePosition] = state;
            }
            else
            {
                PlantCount++;
                _plantStatesByTile[instance.TilePosition] = state;
            }
        }

        var groupedByBatch = _visibleStates
            .GroupBy(state => new VegetationBatchKey(state.Kind, state.Texture, state.OverlayTexture, state.Size))
            .ToDictionary(group => group.Key, group => group.ToList());

        var staleKeys = _batches.Keys.Where(key => !groupedByBatch.ContainsKey(key)).ToArray();
        foreach (var staleKey in staleKeys)
        {
            ReleaseBatch(_batches[staleKey]);
            _batches.Remove(staleKey);
        }

        foreach (var (key, states) in groupedByBatch)
        {
            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = CreateBatch(key);
                _batches[key] = batch;
            }

            ApplyInstances(batch.Main, states, scaleMultiplier: 1f);
        }

        TerrainRenderStats.RecordVegetationFrame(TreeCount, PlantCount);
    }

    public bool TryResolveHoveredTile(Camera3D? camera, Viewport viewport, Vector2 screenPosition, out Vector2I tile)
    {
        tile = default;
        if (camera is null)
            return false;

        VegetationPickCandidate? best = null;
        foreach (var state in _visibleStates)
        {
            if (!TryProjectState(camera, viewport, state, out var projection))
                continue;

            if (!projection.ScreenRect.HasPoint(screenPosition))
                continue;

            if (!HitsOpaquePixel(state.Texture, projection.ScreenRect, screenPosition))
                continue;

            var screenDistanceSquared = projection.ScreenCenter.DistanceSquaredTo(screenPosition);
            if (best is null
                || projection.DepthSquared < best.Value.DepthSquared
                || (Mathf.IsEqualApprox(projection.DepthSquared, best.Value.DepthSquared) && screenDistanceSquared < best.Value.ScreenDistanceSquared))
            {
                best = new VegetationPickCandidate(state, projection.DepthSquared, screenDistanceSquared);
            }
        }

        if (best is not VegetationPickCandidate resolved)
            return false;

        tile = new Vector2I(resolved.State.TilePosition.X, resolved.State.TilePosition.Y);
        return true;
    }

    public bool TryGetDebugProbe(Camera3D? camera, Viewport viewport, out Vector2 screenPosition, out Vector2I tile)
        => TryGetDebugProbe(camera, viewport, probeValidator: null, out screenPosition, out tile);

    public bool TryGetDebugProbe(Camera3D? camera, Viewport viewport, Func<Vector2, Vec3i, bool>? probeValidator, out Vector2 screenPosition, out Vector2I tile)
    {
        screenPosition = default;
        tile = default;
        if (camera is null)
            return false;

        foreach (var state in _visibleStates)
        {
            if (!TryFindStableProbe(camera, viewport, state, out screenPosition))
                continue;

            if (probeValidator is not null && !probeValidator(screenPosition, state.TilePosition))
                continue;

            tile = new Vector2I(state.TilePosition.X, state.TilePosition.Y);
            return true;
        }

        return false;
    }

    public bool TryGetTreeTexture(Vec3i tilePosition, out Texture2D? texture)
    {
        if (_treeStatesByTile.TryGetValue(tilePosition, out var state))
        {
            texture = state.Texture;
            return true;
        }

        texture = null;
        return false;
    }

    public bool TryGetTreeProbe(Vec3i tilePosition, Camera3D? camera, Viewport viewport, out Vector2 screenPosition)
        => TryGetTreeProbe(tilePosition, camera, viewport, probeValidator: null, out screenPosition);

    public bool TryGetTreeProbe(Vec3i tilePosition, Camera3D? camera, Viewport viewport, Func<Vector2, Vec3i, bool>? probeValidator, out Vector2 screenPosition)
    {
        screenPosition = default;
        if (camera is null || !_treeStatesByTile.TryGetValue(tilePosition, out var state))
            return false;

        if (!TryFindStableProbe(camera, viewport, state, out screenPosition))
            return false;

        if (probeValidator is not null && !probeValidator(screenPosition, state.TilePosition))
            return false;

        return true;
    }

    public bool TryGetTreeRenderedSize(Vec3i tilePosition, out Vector2 size)
    {
        size = default;
        if (!_treeStatesByTile.TryGetValue(tilePosition, out var state))
            return false;

        var key = new VegetationBatchKey(state.Kind, state.Texture, state.OverlayTexture, state.Size);
        if (!_batches.TryGetValue(key, out var batch) || batch.Main.Multimesh?.Mesh is not QuadMesh mesh)
            return false;

        size = mesh.Size;
        return true;
    }

    public bool TryGetTreeTransparencyMode(Vec3i tilePosition, out BaseMaterial3D.TransparencyEnum transparency)
    {
        transparency = default;
        if (!_treeStatesByTile.TryGetValue(tilePosition, out var state))
            return false;

        var key = new VegetationBatchKey(state.Kind, state.Texture, state.OverlayTexture, state.Size);
        if (!_batches.TryGetValue(key, out var batch) || batch.Main.MaterialOverride is not BaseMaterial3D material)
            return false;

        transparency = material.Transparency;
        return true;
    }

    public bool TryGetTreeHasOverlayPass(Vec3i tilePosition, out bool hasOverlayPass)
    {
        hasOverlayPass = false;
        if (!_treeStatesByTile.TryGetValue(tilePosition, out var state))
            return false;

        var key = new VegetationBatchKey(state.Kind, state.Texture, state.OverlayTexture, state.Size);
        if (!_batches.TryGetValue(key, out var batch) || batch.Main.MaterialOverride is not Material material)
            return false;

        hasOverlayPass = material.NextPass is not null;
        return true;
    }

    public bool TryGetHoverPresentation(Vec3i tilePosition, VegetationInstanceKind kind, out VegetationHoverPresentation snapshot)
    {
        snapshot = default;
        if (!TryGetVisualState(tilePosition, kind, out var state))
            return false;

        snapshot = new VegetationHoverPresentation(
            state.TilePosition,
            state.Kind,
            state.Texture,
            state.OverlayTexture,
            state.FootPosition,
            state.Size);
        return true;
    }

    public bool HasVisualState(Vec3i tilePosition)
        => _treeStatesByTile.ContainsKey(tilePosition)
           || _plantStatesByTile.ContainsKey(tilePosition)
           || TryGetVisibleStateFallback(tilePosition, out _);

    private VegetationBatchState CreateBatch(VegetationBatchKey key)
    {
        var root = new Node3D
        {
            Name = key.OverlayTexture is null
                ? $"{key.Kind}_{key.Texture.GetRid().Id}"
                : $"{key.Kind}_{key.Texture.GetRid().Id}_{key.OverlayTexture.GetRid().Id}",
        };
        var main = CreateBatchNode("Main", CreateMaterial(key.Texture, key.OverlayTexture, Colors.White, BillboardRenderPriority), key.Size);

        root.AddChild(main);
        AddChild(root);

        return new VegetationBatchState(root, main, key.Size);
    }

    private MultiMeshInstance3D CreateBatchNode(string name, Material material, Vector2 meshSize)
    {
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = false,
            Mesh = new QuadMesh { Size = meshSize },
        };

        return new MultiMeshInstance3D
        {
            Name = name,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            Multimesh = multimesh,
        };
    }

    private void ApplyInstances(MultiMeshInstance3D instance, IReadOnlyList<VegetationVisualState> states, float scaleMultiplier)
    {
        var multimesh = instance.Multimesh;
        if (multimesh is null)
            throw new InvalidOperationException("Vegetation batch multimesh should be initialized before applying instances.");

        multimesh.InstanceCount = states.Count;
        multimesh.VisibleInstanceCount = states.Count;
        for (var index = 0; index < states.Count; index++)
            multimesh.SetInstanceTransform(index, BuildInstanceTransform(states[index].FootPosition, states[index].Size, scaleMultiplier));

        instance.Visible = _isActive && states.Count > 0;
    }

    private void ApplyBatchVisibility(VegetationBatchState batch)
    {
        batch.Root.Visible = _isActive;
        batch.Main.Visible = _isActive && batch.Main.Multimesh?.VisibleInstanceCount > 0;
    }

    private static Transform3D BuildInstanceTransform(Vector3 footPosition, Vector2 size, float scaleMultiplier)
    {
        var scale = new Vector3(scaleMultiplier, scaleMultiplier, 1f);
        return new Transform3D(
            Basis.Identity.Scaled(scale),
            footPosition + new Vector3(0f, size.Y * scaleMultiplier * 0.5f, 0f));
    }

    private static StandardMaterial3D CreateMaterial(Texture2D texture, Texture2D? overlayTexture, Color tint, int renderPriority)
    {
        var material = new StandardMaterial3D
        {
            // Vegetation uses authored cutout sprites, so alpha scissor is the stable
            // path for overlapping batched billboards.
            Transparency = VegetationTransparencyMode,
            AlphaScissorThreshold = AlphaScissorThreshold,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            AlbedoColor = tint,
            RenderPriority = renderPriority,
        };

        if (overlayTexture is not null)
            material.NextPass = CreateOverlayMaterial(overlayTexture, tint);

        return material;
    }

    private static StandardMaterial3D CreateOverlayMaterial(Texture2D texture, Color tint)
    {
        return new StandardMaterial3D
        {
            Transparency = VegetationTransparencyMode,
            AlphaScissorThreshold = AlphaScissorThreshold,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            AlbedoColor = tint,
            RenderPriority = BillboardOverlayRenderPriority,
        };
    }

    private bool TryFindStableProbe(Camera3D camera, Viewport viewport, VegetationVisualState state, out Vector2 screenPosition)
    {
        screenPosition = default;
        if (!TryProjectState(camera, viewport, state, out var projection))
            return false;

        if (!TryFindOpaqueScreenProbe(state.Texture, projection.ScreenRect, out screenPosition))
            return false;

        if (TryResolveHoveredTile(camera, viewport, screenPosition, out var resolvedTile)
            && resolvedTile == new Vector2I(state.TilePosition.X, state.TilePosition.Y))
        {
            return true;
        }

        return false;
    }

    private static bool TryFindOpaqueScreenProbe(Texture2D texture, Rect2 screenRect, out Vector2 screenPosition)
    {
        screenPosition = default;
        if (!TryGetTextureImage(texture, out var image))
            return false;

        var width = image.GetWidth();
        var height = image.GetHeight();
        if (width <= 0 || height <= 0 || screenRect.Size.X <= Mathf.Epsilon || screenRect.Size.Y <= Mathf.Epsilon)
            return false;

        var centerX = width / 2;
        for (var y = height - 1; y >= 0; y--)
        {
            for (var offset = 0; offset < width; offset++)
            {
                var x = centerX + ResolveProbeOffset(offset);
                if (x < 0 || x >= width)
                    continue;

                if (image.GetPixel(x, y).A <= AlphaHitThreshold)
                    continue;

                var u = (x + 0.5f) / width;
                var v = (y + 0.5f) / height;
                screenPosition = screenRect.Position + new Vector2(screenRect.Size.X * u, screenRect.Size.Y * v);
                return true;
            }
        }

        return false;
    }

    private static int ResolveProbeOffset(int index)
    {
        if (index == 0)
            return 0;

        var step = (index + 1) / 2;
        return (index % 2 == 0) ? step : -step;
    }

    private static bool HitsOpaquePixel(Texture2D texture, Rect2 screenRect, Vector2 screenPosition)
    {
        if (!TryGetTextureImage(texture, out var image))
            return true;

        var width = image.GetWidth();
        var height = image.GetHeight();
        if (width <= 0 || height <= 0 || screenRect.Size.X <= Mathf.Epsilon || screenRect.Size.Y <= Mathf.Epsilon)
            return false;

        var u = Mathf.Clamp((screenPosition.X - screenRect.Position.X) / screenRect.Size.X, 0f, 0.999999f);
        var v = Mathf.Clamp((screenPosition.Y - screenRect.Position.Y) / screenRect.Size.Y, 0f, 0.999999f);
        var pixelX = Mathf.Clamp((int)Mathf.Floor(u * width), 0, width - 1);
        var pixelY = Mathf.Clamp((int)Mathf.Floor(v * height), 0, height - 1);
        return image.GetPixel(pixelX, pixelY).A > AlphaHitThreshold;
    }

    private static bool TryGetTextureImage(Texture2D texture, out Image image)
    {
        if (TextureImageCache.TryGetValue(texture, out image!))
            return true;

        image = texture.GetImage();
        if (image is null)
            return false;

        TextureImageCache[texture] = image;
        return true;
    }

    private static bool TryProjectState(Camera3D camera, Viewport viewport, VegetationVisualState state, out VegetationProjection projection)
    {
        projection = default;

        var rightAxis = camera.GlobalTransform.Basis.X.Normalized();
        var upAxis = camera.GlobalTransform.Basis.Y.Normalized();
        var centerWorld = state.FootPosition + (upAxis * (state.Size.Y * 0.5f));
        var cameraOrigin = camera.GlobalTransform.Origin;
        var forwardAxis = -camera.GlobalTransform.Basis.Z.Normalized();
        var toCenter = centerWorld - cameraOrigin;
        if (toCenter.Dot(forwardAxis) <= 0f)
            return false;

        var rightWorld = centerWorld + (rightAxis * (state.Size.X * 0.5f));
        var topWorld = centerWorld + (upAxis * (state.Size.Y * 0.5f));
        var screenCenter = camera.UnprojectPosition(centerWorld);
        var rightScreen = camera.UnprojectPosition(rightWorld);
        var topScreen = camera.UnprojectPosition(topWorld);
        var halfWidth = Mathf.Max(3f, Mathf.Abs(rightScreen.X - screenCenter.X) + 2f);
        var halfHeight = Mathf.Max(5f, Mathf.Abs(topScreen.Y - screenCenter.Y) + 2f);
        var screenRect = new Rect2(
            screenCenter - new Vector2(halfWidth, halfHeight),
            new Vector2(halfWidth * 2f, halfHeight * 2f));

        var viewportRect = viewport.GetVisibleRect();
        if (!viewportRect.Intersects(screenRect))
            return false;

        projection = new VegetationProjection(screenCenter, screenRect, toCenter.LengthSquared());
        return true;
    }

    private bool TryGetVisualState(Vec3i tilePosition, VegetationInstanceKind kind, out VegetationVisualState state)
    {
        return kind switch
        {
            VegetationInstanceKind.Tree => _treeStatesByTile.TryGetValue(tilePosition, out state!),
            VegetationInstanceKind.Plant => _plantStatesByTile.TryGetValue(tilePosition, out state!),
            _ => TryGetVisibleStateFallback(tilePosition, out state!),
        };
    }

    private bool TryGetVisibleStateFallback(Vec3i tilePosition, out VegetationVisualState state)
    {
        state = _visibleStates.FirstOrDefault(candidate => candidate.TilePosition == tilePosition)!;
        return state is not null;
    }

    private static void ReleaseBatch(VegetationBatchState batch)
    {
        batch.Main.Multimesh?.Dispose();
        if (batch.Main.MaterialOverride is Material material)
        {
            if (material.NextPass is IDisposable nextPass)
            {
                material.NextPass = null;
                nextPass.Dispose();
            }

            material.Dispose();
        }

        batch.Root.QueueFree();
    }

    private readonly record struct VegetationBatchKey(VegetationInstanceKind Kind, Texture2D Texture, Texture2D? OverlayTexture, Vector2 Size);

    private sealed class VegetationBatchState
    {
        public VegetationBatchState(Node3D root, MultiMeshInstance3D main, Vector2 size)
        {
            Root = root;
            Main = main;
            Size = size;
        }

        public Node3D Root { get; }

        public MultiMeshInstance3D Main { get; }

        public Vector2 Size { get; }
    }

    private sealed class VegetationVisualState
    {
        public VegetationVisualState(VegetationVisualInstance instance)
        {
            TilePosition = instance.TilePosition;
            Kind = instance.Kind;
            Texture = instance.Texture;
            OverlayTexture = instance.OverlayTexture;
            FootPosition = instance.FootPosition;
            Size = instance.Size;
        }

        public Vec3i TilePosition { get; }

        public VegetationInstanceKind Kind { get; }

        public Texture2D Texture { get; }

        public Texture2D? OverlayTexture { get; }

        public Vector3 FootPosition { get; }

        public Vector2 Size { get; }
    }

    private readonly record struct VegetationPickCandidate(
        VegetationVisualState State,
        float DepthSquared,
        float ScreenDistanceSquared);

    private readonly record struct VegetationProjection(
        Vector2 ScreenCenter,
        Rect2 ScreenRect,
        float DepthSquared);
}
