using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

internal static class TerrainSurfaceArrayLibrary
{
    private const int InitialTextureArrayCapacity = 256;
    private const int DefaultMaxTextureArrayLayers = 2048;
    private const int ReservedFallbackLayerBudget = 12;

    private static readonly object Sync = new();
    private static readonly Dictionary<TerrainSurfaceRecipe, TerrainSurfaceArrayEntry> Entries = new();
    private static readonly List<Image> ArrayLayerImages = new();

    private static Texture2DArray? _textureArray;
    private static int _textureArrayCapacity;
    private static int _textureArrayRebuildCount;
    private static int? _maxTextureArrayLayers;
    private static int? _debugMaxTextureArrayLayersOverride;
    private static bool _fallbackLayersInitialized;

    public static int GetOrCreateArrayLayer(
        TileRenderData tile,
        int x,
        int y,
        int z,
        Func<int, int, int, TileRenderData?> tryGetTile,
        Func<string?, string?>? resolveGroundFromMaterial,
        TerrainTransitionSet? cachedGroundTransitions)
    {
        var recipe = TerrainSurfaceRecipeBuilder.Build(tile, x, y, z, tryGetTile, resolveGroundFromMaterial, cachedGroundTransitions);
        return GetOrCreateArrayLayer(recipe);
    }

    public static int GetOrCreateArrayLayer(TerrainSurfaceRecipe recipe)
    {
        var canonicalRecipe = CanonicalizeRecipe(recipe);
        lock (Sync)
        {
            EnsureFallbackEntries();

            if (Entries.TryGetValue(canonicalRecipe, out var existingEntry))
                return EnsureArrayLayer(existingEntry);

            if (!CanAllocateNewLayer())
                return GetOrCreateFallbackArrayLayer(canonicalRecipe);

            return EnsureArrayLayer(GetOrCreateEntry(canonicalRecipe));
        }
    }

    public static Texture2DArray? GetTextureArray()
    {
        lock (Sync)
            return _textureArray;
    }

    public static int GetLayerCount()
    {
        lock (Sync)
            return ArrayLayerImages.Count;
    }

    public static int GetCapacity()
    {
        lock (Sync)
            return _textureArrayCapacity;
    }

    public static int GetArrayRebuildCount()
    {
        lock (Sync)
            return _textureArrayRebuildCount;
    }

    public static int GetMaxSupportedLayerCount()
    {
        lock (Sync)
            return ResolveMaxSupportedLayerCount();
    }

    public static bool ContainsRecipe(TerrainSurfaceRecipe recipe)
    {
        var canonicalRecipe = CanonicalizeRecipe(recipe);
        lock (Sync)
            return Entries.ContainsKey(canonicalRecipe);
    }

    public static void Reset()
    {
        lock (Sync)
        {
            Entries.Clear();
            ArrayLayerImages.Clear();
            _textureArray?.Dispose();
            _textureArray = null;
            _textureArrayCapacity = 0;
            _textureArrayRebuildCount = 0;
            _maxTextureArrayLayers = null;
            _fallbackLayersInitialized = false;
        }
    }

    public static void DebugSetMaxSupportedLayerCountForTesting(int? maxLayerCount)
    {
        lock (Sync)
        {
            _debugMaxTextureArrayLayersOverride = maxLayerCount;
            _maxTextureArrayLayers = null;
        }
    }

    public static void DebugEnsureCapacityForTesting(int requiredLayerCount)
    {
        lock (Sync)
            EnsureTextureArrayCapacity(requiredLayerCount);
    }

    private static TerrainSurfaceRecipe CanonicalizeRecipe(TerrainSurfaceRecipe recipe)
    {
        // Keep the 3D texture-array recipe space bounded. The 2D cache can preserve more exact
        // material variation, but the chunked 3D path must aggressively reuse layers or it can
        // exceed the GPU texture-array budget and make floor fragments disappear.
        return recipe with
        {
            BaseTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(recipe.BaseTileDefId),
            BaseMaterialId = CanonicalizeBaseMaterialId(recipe.BaseTileDefId, recipe.BaseMaterialId),
            NorthEdge = Canonicalize(recipe.NorthEdge),
            SouthEdge = Canonicalize(recipe.SouthEdge),
            WestEdge = Canonicalize(recipe.WestEdge),
            EastEdge = Canonicalize(recipe.EastEdge),
            NorthWestCorner = Canonicalize(recipe.NorthWestCorner),
            NorthEastCorner = Canonicalize(recipe.NorthEastCorner),
            SouthWestCorner = Canonicalize(recipe.SouthWestCorner),
            SouthEastCorner = Canonicalize(recipe.SouthEastCorner),
            LiquidTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(recipe.LiquidTileDefId),
            LiquidDepth = Canonicalize(recipe.LiquidDepth),
            LiquidNorthEdge = Canonicalize(recipe.LiquidNorthEdge),
            LiquidSouthEdge = Canonicalize(recipe.LiquidSouthEdge),
            LiquidWestEdge = Canonicalize(recipe.LiquidWestEdge),
            LiquidEastEdge = Canonicalize(recipe.LiquidEastEdge),
            LiquidNorthWestCorner = Canonicalize(recipe.LiquidNorthWestCorner),
            LiquidNorthEastCorner = Canonicalize(recipe.LiquidNorthEastCorner),
            LiquidSouthWestCorner = Canonicalize(recipe.LiquidSouthWestCorner),
            LiquidSouthEastCorner = Canonicalize(recipe.LiquidSouthEastCorner),
        };
    }

    private static GroundEdgePatchRecipe? Canonicalize(GroundEdgePatchRecipe? patch)
        => patch is GroundEdgePatchRecipe resolved
            ? resolved with
            {
                TileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(resolved.TileDefId),
                MaterialId = CanonicalizeBlendMaterialId(resolved.TileDefId),
                Variant = 0,
            }
            : null;

    private static GroundCornerPatchRecipe? Canonicalize(GroundCornerPatchRecipe? patch)
        => patch is GroundCornerPatchRecipe resolved
            ? resolved with
            {
                TileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(resolved.TileDefId),
                MaterialId = CanonicalizeBlendMaterialId(resolved.TileDefId),
                Variant = 0,
            }
            : null;

    private static LiquidDepthPatchRecipe? Canonicalize(LiquidDepthPatchRecipe? patch)
        => patch is LiquidDepthPatchRecipe resolved
            ? resolved with
            {
                LiquidTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(resolved.LiquidTileDefId),
                Variant = 0,
            }
            : null;

    private static LiquidEdgePatchRecipe? Canonicalize(LiquidEdgePatchRecipe? patch)
        => patch is LiquidEdgePatchRecipe resolved
            ? resolved with
            {
                LiquidTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(resolved.LiquidTileDefId),
                Variant = 0,
            }
            : null;

    private static LiquidCornerPatchRecipe? Canonicalize(LiquidCornerPatchRecipe? patch)
        => patch is LiquidCornerPatchRecipe resolved
            ? resolved with
            {
                LiquidTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(resolved.LiquidTileDefId),
                Variant = 0,
            }
            : null;

    private static TerrainSurfaceArrayEntry GetOrCreateEntry(TerrainSurfaceRecipe recipe)
    {
        if (Entries.TryGetValue(recipe, out var existing))
            return existing;

        var image = TerrainSurfaceRecipeBuilder.ComposeImage(recipe);
        var created = new TerrainSurfaceArrayEntry(image);
        Entries[recipe] = created;
        return created;
    }

    private static int EnsureArrayLayer(TerrainSurfaceArrayEntry entry)
    {
        if (entry.ArrayLayer >= 0)
            return entry.ArrayLayer;

        if (!CanAllocateNewLayer())
            throw new InvalidOperationException("Terrain texture array has no reserved fallback capacity left.");

        entry.ArrayLayer = ArrayLayerImages.Count;
        ArrayLayerImages.Add(entry.Image);
        EnsureTextureArrayCapacity(entry.ArrayLayer + 1);
        _textureArray!.UpdateLayer(entry.Image, entry.ArrayLayer);
        return entry.ArrayLayer;
    }

    private static void EnsureTextureArrayCapacity(int requiredLayerCount)
    {
        var maxSupportedLayerCount = ResolveMaxSupportedLayerCount();
        if (requiredLayerCount > maxSupportedLayerCount)
            throw new InvalidOperationException($"Terrain texture array requested {requiredLayerCount} layers, exceeding renderer limit {maxSupportedLayerCount}.");

        if (_textureArray is not null && requiredLayerCount <= _textureArrayCapacity)
            return;

        var requestedCapacity = _textureArrayCapacity == 0
            ? Math.Max(InitialTextureArrayCapacity, requiredLayerCount)
            : Math.Max(_textureArrayCapacity * 2, requiredLayerCount);
        _textureArrayCapacity = Math.Min(requestedCapacity, maxSupportedLayerCount);

        var images = new Godot.Collections.Array<Image>();
        for (var index = 0; index < _textureArrayCapacity; index++)
        {
            images.Add(index < ArrayLayerImages.Count
                ? ArrayLayerImages[index]
                : CreateEmptyLayerImage());
        }

        var nextTextureArray = new Texture2DArray();
        nextTextureArray.CreateFromImages(images);
        _textureArrayRebuildCount++;

        var previousTextureArray = _textureArray;
        _textureArray = nextTextureArray;
        previousTextureArray?.Dispose();
    }

    private static Image CreateEmptyLayerImage()
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));
        return image;
    }

    private static int ResolveMaxSupportedLayerCount()
    {
        if (_debugMaxTextureArrayLayersOverride is int debugOverride && debugOverride > 0)
            return Math.Max(debugOverride, ReservedFallbackLayerBudget);

        if (_maxTextureArrayLayers is int cached && cached > 0)
            return cached;

        var maxLayerCount = DefaultMaxTextureArrayLayers;
        try
        {
            var renderingDevice = RenderingServer.GetRenderingDevice();
            if (renderingDevice is not null)
            {
                var reportedLimit = Convert.ToInt32(renderingDevice.LimitGet(RenderingDevice.Limit.MaxTextureArrayLayers));
                if (reportedLimit > 0)
                    maxLayerCount = reportedLimit;
            }
        }
        catch
        {
            maxLayerCount = DefaultMaxTextureArrayLayers;
        }

        _maxTextureArrayLayers = Math.Max(maxLayerCount, ReservedFallbackLayerBudget);
        return _maxTextureArrayLayers.Value;
    }

    private static bool CanAllocateNewLayer()
        => ArrayLayerImages.Count < ResolveMaxSupportedLayerCount();

    private static string CanonicalizeBaseMaterialId(string tileDefId, string materialId)
    {
        var normalizedTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(tileDefId);
        return normalizedTileDefId switch
        {
            TileDefIds.Grass or TileDefIds.Soil or TileDefIds.SoilWall or TileDefIds.Sand or TileDefIds.Mud or TileDefIds.Snow
                or TileDefIds.Water or TileDefIds.Magma or TileDefIds.Obsidian => string.Empty,
            TileDefIds.WoodFloor => MaterialIds.Wood,
            _ => TerrainSurfaceRecipeBuilder.NormalizeKey(materialId),
        };
    }

    private static string CanonicalizeBlendMaterialId(string tileDefId)
    {
        var normalizedTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(tileDefId);
        return normalizedTileDefId switch
        {
            TileDefIds.StoneFloor or TileDefIds.StoneWall or TileDefIds.StoneBrick => MaterialIds.Granite,
            TileDefIds.WoodFloor => MaterialIds.Wood,
            _ => string.Empty,
        };
    }

    private static void EnsureFallbackEntries()
    {
        if (_fallbackLayersInitialized)
            return;

        foreach (var recipe in EnumerateReservedFallbackRecipes())
            EnsureArrayLayer(GetOrCreateEntry(recipe));

        _fallbackLayersInitialized = true;
    }

    private static IEnumerable<TerrainSurfaceRecipe> EnumerateReservedFallbackRecipes()
    {
        yield return CreateFallbackRecipe(TileDefIds.Grass, string.Empty);
        yield return CreateFallbackRecipe(TileDefIds.Soil, string.Empty);
        yield return CreateFallbackRecipe(TileDefIds.Sand, string.Empty);
        yield return CreateFallbackRecipe(TileDefIds.Mud, string.Empty);
        yield return CreateFallbackRecipe(TileDefIds.Snow, string.Empty);
        yield return CreateFallbackRecipe(TileDefIds.StoneFloor, MaterialIds.Granite);
        yield return CreateFallbackRecipe(TileDefIds.StoneWall, MaterialIds.Granite);
        yield return CreateFallbackRecipe(TileDefIds.StoneBrick, MaterialIds.Granite);
        yield return CreateFallbackRecipe(TileDefIds.Obsidian, string.Empty);
        yield return CreateFallbackRecipe(TileDefIds.WoodFloor, MaterialIds.Wood);
        yield return CreateLiquidFallbackRecipe(TileDefIds.Soil, string.Empty, TileDefIds.Water, fluidLevel: 7);
        yield return CreateLiquidFallbackRecipe(TileDefIds.Obsidian, string.Empty, TileDefIds.Magma, fluidLevel: 7);
    }

    private static TerrainSurfaceRecipe CreateFallbackRecipe(string baseTileDefId, string baseMaterialId)
        => new(
            BaseTileDefId: baseTileDefId,
            BaseMaterialId: baseMaterialId,
            NorthEdge: null,
            SouthEdge: null,
            WestEdge: null,
            EastEdge: null,
            NorthWestCorner: null,
            NorthEastCorner: null,
            SouthWestCorner: null,
            SouthEastCorner: null,
            LiquidTileDefId: string.Empty,
            FluidLevel: 0,
            LiquidDepth: null,
            LiquidNorthEdge: null,
            LiquidSouthEdge: null,
            LiquidWestEdge: null,
            LiquidEastEdge: null,
            LiquidNorthWestCorner: null,
            LiquidNorthEastCorner: null,
            LiquidSouthWestCorner: null,
            LiquidSouthEastCorner: null);

    private static TerrainSurfaceRecipe CreateLiquidFallbackRecipe(string baseTileDefId, string baseMaterialId, string liquidTileDefId, byte fluidLevel)
        => new(
            BaseTileDefId: baseTileDefId,
            BaseMaterialId: baseMaterialId,
            NorthEdge: null,
            SouthEdge: null,
            WestEdge: null,
            EastEdge: null,
            NorthWestCorner: null,
            NorthEastCorner: null,
            SouthWestCorner: null,
            SouthEastCorner: null,
            LiquidTileDefId: liquidTileDefId,
            FluidLevel: fluidLevel,
            LiquidDepth: new LiquidDepthPatchRecipe(liquidTileDefId, fluidLevel, SameCount: 4, Variant: 0),
            LiquidNorthEdge: null,
            LiquidSouthEdge: null,
            LiquidWestEdge: null,
            LiquidEastEdge: null,
            LiquidNorthWestCorner: null,
            LiquidNorthEastCorner: null,
            LiquidSouthWestCorner: null,
            LiquidSouthEastCorner: null);

    private static TerrainSurfaceRecipe BuildCapacityFallbackRecipe(TerrainSurfaceRecipe recipe)
    {
        if (recipe.LiquidTileDefId == TileDefIds.Water)
            return CreateLiquidFallbackRecipe(TileDefIds.Soil, string.Empty, TileDefIds.Water, (byte)Math.Max(recipe.FluidLevel, (byte)1));

        if (recipe.LiquidTileDefId == TileDefIds.Magma)
            return CreateLiquidFallbackRecipe(TileDefIds.Obsidian, string.Empty, TileDefIds.Magma, (byte)Math.Max(recipe.FluidLevel, (byte)1));

        var fallbackBaseTileDefId = ResolveFallbackBaseTileDefId(recipe.BaseTileDefId);
        var fallbackBaseMaterialId = fallbackBaseTileDefId switch
        {
            TileDefIds.StoneFloor or TileDefIds.StoneWall or TileDefIds.StoneBrick => MaterialIds.Granite,
            TileDefIds.WoodFloor => MaterialIds.Wood,
            _ => string.Empty,
        };

        return CreateFallbackRecipe(fallbackBaseTileDefId, fallbackBaseMaterialId);
    }

    private static string ResolveFallbackBaseTileDefId(string baseTileDefId)
    {
        var normalizedTileDefId = TerrainSurfaceRecipeBuilder.NormalizeKey(baseTileDefId);
        return normalizedTileDefId switch
        {
            TileDefIds.Grass => TileDefIds.Grass,
            TileDefIds.Soil or TileDefIds.SoilWall => TileDefIds.Soil,
            TileDefIds.Sand => TileDefIds.Sand,
            TileDefIds.Mud => TileDefIds.Mud,
            TileDefIds.Snow => TileDefIds.Snow,
            TileDefIds.WoodFloor => TileDefIds.WoodFloor,
            TileDefIds.StoneWall => TileDefIds.StoneWall,
            TileDefIds.StoneBrick => TileDefIds.StoneBrick,
            TileDefIds.Obsidian => TileDefIds.Obsidian,
            _ => TileDefIds.StoneFloor,
        };
    }

    private static int GetOrCreateFallbackArrayLayer(TerrainSurfaceRecipe recipe)
    {
        var fallbackRecipe = BuildCapacityFallbackRecipe(recipe);
        if (Entries.TryGetValue(fallbackRecipe, out var existingFallbackEntry))
            return EnsureArrayLayer(existingFallbackEntry);

        foreach (var reservedRecipe in EnumerateReservedFallbackRecipes())
        {
            if (Entries.TryGetValue(reservedRecipe, out var reservedEntry))
                return EnsureArrayLayer(reservedEntry);
        }

        return EnsureArrayLayer(GetOrCreateEntry(fallbackRecipe));
    }

    private sealed class TerrainSurfaceArrayEntry
    {
        public TerrainSurfaceArrayEntry(Image image)
        {
            Image = image;
        }

        public Image Image { get; }

        public int ArrayLayer { get; set; } = -1;
    }
}