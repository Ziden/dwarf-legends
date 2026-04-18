using System;
using System.Collections.Generic;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class TerrainDetailOverlayLibrary
{
    private const int InitialTextureArrayCapacity = 64;

    private static readonly object Sync = new();
    private static readonly Dictionary<TerrainDetailRecipe, TerrainDetailEntry> Entries = new();
    private static readonly List<Image> ArrayLayerImages = new();

    private static Texture2DArray? _textureArray;
    private static int _textureArrayCapacity;
    private static int _textureArrayRebuildCount;

    public static bool TryGetOrCreateArrayLayer(TileRenderData tile, int x, int y, int z, out int layer)
    {
        layer = -1;
        var recipe = BuildRecipe(tile, x, y, z);
        if (recipe is not TerrainDetailRecipe resolved)
            return false;

        lock (Sync)
        {
            layer = EnsureArrayLayer(GetOrCreateEntry(resolved));
            return true;
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
        }
    }

    private static TerrainDetailRecipe? BuildRecipe(TileRenderData tile, int x, int y, int z)
    {
        if (string.IsNullOrWhiteSpace(tile.OreItemDefId))
            return null;

        return new TerrainDetailRecipe(
            OreItemDefId: NormalizeKey(tile.OreItemDefId),
            OreVariant: OreOverlayRenderer.ResolveVariant(tile.OreItemDefId, x, y, z));
    }

    private static TerrainDetailEntry GetOrCreateEntry(TerrainDetailRecipe recipe)
    {
        if (Entries.TryGetValue(recipe, out var existing))
            return existing;

        var image = ComposeOverlayImage(recipe);
        var created = new TerrainDetailEntry(image);
        Entries[recipe] = created;
        return created;
    }

    private static int EnsureArrayLayer(TerrainDetailEntry entry)
    {
        if (entry.ArrayLayer >= 0)
            return entry.ArrayLayer;

        entry.ArrayLayer = ArrayLayerImages.Count;
        ArrayLayerImages.Add(entry.Image);
        EnsureTextureArrayCapacity(entry.ArrayLayer + 1);
        _textureArray!.UpdateLayer(entry.Image, entry.ArrayLayer);
        return entry.ArrayLayer;
    }

    private static void EnsureTextureArrayCapacity(int requiredLayerCount)
    {
        if (_textureArray is not null && requiredLayerCount <= _textureArrayCapacity)
            return;

        _textureArrayCapacity = _textureArrayCapacity == 0
            ? Math.Max(InitialTextureArrayCapacity, requiredLayerCount)
            : Math.Max(_textureArrayCapacity * 2, requiredLayerCount);

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

    private static Image ComposeOverlayImage(TerrainDetailRecipe recipe)
    {
        var image = CreateEmptyLayerImage();
        OreOverlayRenderer.Draw(
            image,
            new Rect2I(0, 0, PixelArtFactory.Size, PixelArtFactory.Size),
            recipe.OreItemDefId,
            recipe.OreVariant);
        return image;
    }

    private static Image CreateEmptyLayerImage()
    {
        var image = Image.CreateEmpty(PixelArtFactory.Size, PixelArtFactory.Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));
        return image;
    }

    private static string NormalizeKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private sealed class TerrainDetailEntry
    {
        public TerrainDetailEntry(Image image)
        {
            Image = image;
        }

        public Image Image { get; }

        public int ArrayLayer { get; set; } = -1;
    }

    private readonly record struct TerrainDetailRecipe(
        string OreItemDefId,
        byte OreVariant);
}
