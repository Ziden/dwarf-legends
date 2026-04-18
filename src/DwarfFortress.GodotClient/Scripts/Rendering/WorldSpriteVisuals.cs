using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.World;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public readonly record struct WorldSpriteVisual(Texture2D Texture, Vector2 PixelSize)
{
    public Vector2 WorldSize => PixelSize / (float)RenderCache.TileSize;
}

public static class WorldSpriteVisuals
{
    private static readonly Vector2 DwarfPixelSize = new(42f, 46f);
    private static readonly Vector2 CreaturePixelSize = new(40f, 40f);
    private static readonly Vector2 ItemPixelSize = new(32f, 32f);
    private static readonly Vector2 PlantPixelSize = new(RenderCache.TileSize, RenderCache.TileSize);
    private static readonly Vector2 TreePixelSize = new(RenderCache.TileSize, RenderCache.TileSize * 2f);

    public static WorldSpriteVisual Dwarf(DwarfAppearanceComponent appearance)
        => Dwarf(appearance, DwarfSpritePose.Idle());

    internal static WorldSpriteVisual Dwarf(DwarfAppearanceComponent appearance, DwarfSpritePose pose)
        => new(PixelArtFactory.GetDwarf(appearance, pose), DwarfPixelSize);

    public static WorldSpriteVisual Creature(string defId)
        => Creature(defId, CreatureSpritePose.Idle());

    internal static WorldSpriteVisual Creature(string defId, CreatureSpritePose pose)
        => new(PixelArtFactory.GetEntity(defId, pose), CreaturePixelSize);

    public static WorldSpriteVisual Item(string defId, string? materialId = null)
        => new(PixelArtFactory.GetItem(defId, materialId), ItemPixelSize);

    public static WorldSpriteVisual Tree(string? speciesId)
        => new(PixelArtFactory.GetTile(TileDefIds.Tree, speciesId), TreePixelSize);

    public static WorldSpriteVisual Tree()
        => new(PixelArtFactory.GetTile(TileDefIds.Tree), TreePixelSize);

    public static bool TryTreeCanopyOverlay(string? plantDefId, byte growthStage, byte yieldLevel, byte seedLevel, out WorldSpriteVisual visual)
    {
        if (string.IsNullOrWhiteSpace(plantDefId))
        {
            visual = default;
            return false;
        }

        visual = new WorldSpriteVisual(
            PixelArtFactory.GetPlantOverlay(plantDefId, growthStage, yieldLevel, seedLevel),
            TreePixelSize);
        return true;
    }

    public static bool TryTreeWithPlantOverlay(string? speciesId, string? plantDefId, byte growthStage, byte yieldLevel, byte seedLevel, out WorldSpriteVisual visual)
    {
        if (string.IsNullOrWhiteSpace(plantDefId)
            || !PixelArtFactory.TryGetPlantOverlay(plantDefId, growthStage, yieldLevel, seedLevel, out var overlayTexture)
            || overlayTexture is null)
        {
            visual = default;
            return false;
        }

        visual = new WorldSpriteVisual(
            PixelArtFactory.GetTreeWithOverlay(speciesId, plantDefId, growthStage, yieldLevel, seedLevel),
            TreePixelSize);
        return true;
    }

    public static bool TryPlantOverlay(string? plantDefId, byte growthStage, byte yieldLevel, byte seedLevel, out WorldSpriteVisual visual)
    {
        if (string.IsNullOrWhiteSpace(plantDefId))
        {
            visual = default;
            return false;
        }

        visual = new WorldSpriteVisual(
            PixelArtFactory.GetPlantOverlay(plantDefId, growthStage, yieldLevel, seedLevel),
            PlantPixelSize);
        return true;
    }
}
