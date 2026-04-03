using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Content;
using DwarfFortress.WorldGen.Ids;
using DwarfFortress.WorldGen.Regions;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

public static class PixelArtFactory
{
    public const int Size = 64;
    private const string UseSpritesEnvVar = "DF_USE_SPRITES";
    private const string Strict2DArtEnvVar = "DF_STRICT_2D_ART";
    private const int DefaultOutlineThickness = 2;
    private readonly record struct TilePalette(Color Base, Color Shadow, Color Highlight);
    private readonly record struct PlantPalette(Color Stem, Color Leaf, Color Accent, Color Outline, bool Canopy);

    private enum RegionGroundClass : byte
    {
        Grass = 0,
        Soil = 1,
        Sand = 2,
        Mud = 3,
        Snow = 4,
        Stone = 5,
    }

    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static readonly HashSet<string> StrictResolvedKeys = new();
    private static readonly DwarfAppearanceComponent DefaultDwarfAppearance = DwarfAppearanceComponent.CreateDefault(0, "default", new DwarfFortress.GameLogic.Core.Vec3i(0, 0, 0));
    public static readonly bool UseSprites = ParseFeatureFlag(UseSpritesEnvVar);
    public static readonly bool Strict2DArt = ParseFeatureFlag(Strict2DArtEnvVar);

    public static Color GetWorldGenRiverColor(float alpha = 0.86f)
        => WithAlpha(new Color(0.10f, 0.44f, 0.92f), alpha);

    public static Color GetWorldGenLakeColor(float alpha = 0.85f)
        => WithAlpha(new Color(0.14f, 0.48f, 0.86f), alpha);

    public static Color GetWorldGenRoadColor(float alpha = 0.82f)
        => WithAlpha(new Color(0.78f, 0.60f, 0.30f), alpha);

    public static Color GetWorldGenSettlementColor(float alpha = 0.92f)
        => WithAlpha(new Color(1.00f, 0.90f, 0.10f), alpha);

    public static Color GetWorldGenSelectionColor(float alpha = 0.95f)
        => WithAlpha(new Color(1.00f, 0.95f, 0.00f), alpha);

    public static Color GetAquiferOverlayColor(float alpha = 0.20f)
        => WithAlpha(new Color(0.25f, 0.62f, 0.98f), alpha);

    public static Color GetWorldGenMismatchColor(float alpha = 0.45f)
        => WithAlpha(new Color(0.95f, 0.12f, 0.12f), alpha);

    public static Color GetWorldGenResolvedColor(float alpha = 0.20f)
        => WithAlpha(new Color(0.20f, 0.92f, 0.45f), alpha);

    public static Color GetWorldGenDeltaPositiveColor(float alpha = 0.56f)
        => WithAlpha(new Color(0.95f, 0.26f, 0.20f), alpha);

    public static Color GetWorldGenDeltaNegativeColor(float alpha = 0.56f)
        => WithAlpha(new Color(0.18f, 0.50f, 0.98f), alpha);

    public static Texture2D GetDwarf(DwarfAppearanceComponent appearance)
    {
        var key = $"dwarf:{appearance.Signature}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        return Cache[key] = MakeDwarf(appearance);
    }

    public static Texture2D GetDwarf(DwarfAppearanceView appearance)
    {
        var key = $"dwarf:view:{appearance.HairType}:{appearance.HairColor}:{appearance.BeardType}:{appearance.BeardColor}:{appearance.EyeType}:{appearance.NoseType}:{appearance.MouthType}:{appearance.FaceType}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var component = new DwarfAppearanceComponent
        {
            HairType = ParseEnum(appearance.HairType, DwarfHairType.Crop),
            HairColor = ParseEnum(appearance.HairColor, DwarfHairColor.Chestnut),
            BeardType = ParseEnum(appearance.BeardType, DwarfBeardType.Short),
            BeardColor = ParseEnum(appearance.BeardColor, DwarfHairColor.Chestnut),
            EyeType = ParseEnum(appearance.EyeType, DwarfEyeType.Dot),
            NoseType = ParseEnum(appearance.NoseType, DwarfNoseType.Button),
            MouthType = ParseEnum(appearance.MouthType, DwarfMouthType.Neutral),
            FaceType = ParseEnum(appearance.FaceType, DwarfFaceType.Round),
        };

        return Cache[key] = MakeDwarf(component);
    }

    public static Texture2D GetUiIcon(string iconId)
    {
        var key = $"ui:{iconId}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        return Cache[key] = iconId switch
        {
            UiIconIds.Pickaxe => MakePickaxeIcon(),
            UiIconIds.Hand => MakeHandIcon(),
            UiIconIds.Cancel => MakeCancelIcon(),
            UiIconIds.Zone => MakeZoneIcon(),
            UiIconIds.Build => MakeBuildIcon(),
            UiIconIds.Speed1 => MakeSpeedIcon(1),
            UiIconIds.Speed3 => MakeSpeedIcon(2),
            UiIconIds.Speed5 => MakeSpeedIcon(3),
            UiIconIds.Pause => MakePauseIcon(),
            UiIconIds.Book => MakeBookIcon(),
            UiIconIds.Fortress => MakeFortressIcon(),
            UiIconIds.Calendar => MakeCalendarIcon(),
            UiIconIds.Migration => MakeMigrationIcon(),
            UiIconIds.Banner => MakeBannerIcon(),
            UiIconIds.Threat => MakeThreatIcon(),
            UiIconIds.Mood => MakeMoodIcon(),
            UiIconIds.Need => MakeNeedIcon(),
            UiIconIds.Death => MakeDeathIcon(),
            UiIconIds.Combat => MakeCombatIcon(),
            UiIconIds.Flood => MakeFloodIcon(),
            UiIconIds.Wildlife => MakeWildlifeIcon(),
            _ => MakeCrate(new Color(0.62f, 0.62f, 0.66f)),
        };
    }

    public static bool CanResolveTile(string tileDefId, string? materialId = null)
        => TryGetTile(tileDefId, materialId, out _);

    public static bool CanResolvePlantOverlay(string plantDefId)
        => TryResolvePlantPalette(plantDefId, out _);

    public static bool CanResolveEntity(string entityDefId)
        => TryGetEntity(entityDefId, out _);

    public static bool CanResolveItem(string itemDefId, string? materialId = null)
        => TryGetItem(itemDefId, materialId, out _);

    public static bool CanResolveBuilding(string buildingDefId)
        => TryGetBuilding(buildingDefId, out _);

    public static Texture2D GetTile(string tileDefId, string? materialId = null)
    {
        if (TryGetTile(tileDefId, materialId, out var texture) && texture is not null)
            return texture;

        var normalizedMaterialId = NormalizeMaterialId(materialId);
        if (Strict2DArt)
            throw CreateMissingArtException("tile", tileDefId, normalizedMaterialId);

        var key = normalizedMaterialId is null ? $"tile:{tileDefId}" : $"tile:{tileDefId}:{normalizedMaterialId}";
        StrictResolvedKeys.Remove(key);
        return Cache[key] = MakeStoneFloor(new Color(0.48f, 0.48f, 0.52f), new Color(0.36f, 0.36f, 0.40f), new Color(0.64f, 0.64f, 0.70f));
    }

    public static bool TryGetTile(string tileDefId, string? materialId, out Texture2D? texture)
    {
        var normalizedMaterialId = NormalizeMaterialId(materialId);
        var key = normalizedMaterialId is null ? $"tile:{tileDefId}" : $"tile:{tileDefId}:{normalizedMaterialId}";
        if (StrictResolvedKeys.Contains(key) && Cache.TryGetValue(key, out var cached))
        {
            texture = cached;
            return true;
        }

        if (UseSprites && SpriteRegistry.TryGetTile(tileDefId, out var sprite) && sprite is not null)
        {
            texture = Cache[key] = sprite;
            StrictResolvedKeys.Add(key);
            return true;
        }

        texture = tileDefId switch
        {
            TileDefIds.Empty => MakeFlatTile(new Color(0f, 0f, 0f, 0f)),
            TileDefIds.Tree => MakeTree(),
            TileDefIds.Water => MakeWater(),
            TileDefIds.Magma => MakeMagma(),
            TileDefIds.Staircase => MakeStair(),
            TileDefIds.Ramp => MakeRampTile(),
            TileDefIds.StoneFloor => MakeStoneFloorPalette(normalizedMaterialId),
            TileDefIds.WoodFloor => MakeFloor(new Color(0.53f, 0.35f, 0.18f), new Color(0.41f, 0.25f, 0.12f)),
            TileDefIds.StoneBrick => MakeBrickFloor(new Color(0.56f, 0.56f, 0.60f), new Color(0.35f, 0.35f, 0.40f)),
            TileDefIds.Grass => MakeGrassFloorPalette(),
            TileDefIds.Sand => MakeSandFloorPalette(normalizedMaterialId),
            TileDefIds.Mud => MakeMudFloorPalette(normalizedMaterialId),
            TileDefIds.Snow => MakeSnowFloorPalette(normalizedMaterialId),
            TileDefIds.Soil => MakeSoilFloorPalette(normalizedMaterialId),
            TileDefIds.Obsidian => MakeStoneFloor(new Color(0.20f, 0.18f, 0.24f), new Color(0.13f, 0.11f, 0.16f), new Color(0.31f, 0.28f, 0.37f)),
            TileDefIds.StoneWall => MakeRockWallForMaterial(normalizedMaterialId),
            TileDefIds.SoilWall => MakeRockWall(new Color(0.42f, 0.30f, 0.19f), new Color(0.31f, 0.22f, 0.14f), new Color(0.54f, 0.40f, 0.27f)),
            _ => null,
        };

        if (texture is null)
            return false;

        Cache[key] = texture;
        StrictResolvedKeys.Add(key);
        return true;
    }

    public static Texture2D GetPlantOverlay(string plantDefId, byte growthStage, byte yieldLevel = 0, byte seedLevel = 0)
    {
        if (TryGetPlantOverlay(plantDefId, growthStage, yieldLevel, seedLevel, out var texture) && texture is not null)
            return texture;

        if (Strict2DArt)
            throw CreateMissingArtException("plant overlay", plantDefId);

        var key = $"plant:{plantDefId}:g{growthStage}:y{yieldLevel}:s{seedLevel}";
        StrictResolvedKeys.Remove(key);
        return Cache[key] = MakePlantOverlay(ResolvePlantPaletteOrFallback(plantDefId), growthStage, yieldLevel, seedLevel);
    }

    public static bool TryGetPlantOverlay(string plantDefId, byte growthStage, byte yieldLevel, byte seedLevel, out Texture2D? texture)
    {
        var key = $"plant:{plantDefId}:g{growthStage}:y{yieldLevel}:s{seedLevel}";
        if (StrictResolvedKeys.Contains(key) && Cache.TryGetValue(key, out var cached))
        {
            texture = cached;
            return true;
        }

        if (!TryResolvePlantPalette(plantDefId, out var palette))
        {
            texture = null;
            return false;
        }

        texture = Cache[key] = MakePlantOverlay(palette, growthStage, yieldLevel, seedLevel);
        StrictResolvedKeys.Add(key);
        return true;
    }

    public static Color GetRepresentativeTileColor(string tileDefId, string? materialId = null)
    {
        var normalizedMaterialId = NormalizeMaterialId(materialId);
        return tileDefId switch
        {
            TileDefIds.Tree => new Color(0.18f, 0.62f, 0.26f),
            TileDefIds.Water => new Color(0.05f, 0.37f, 0.85f),
            TileDefIds.Magma => new Color(0.88f, 0.32f, 0.08f),
            TileDefIds.Staircase => new Color(0.96f, 0.60f, 0.04f),
            TileDefIds.Grass => ResolveGrassPalette().Base,
            TileDefIds.Sand => ResolveSandPalette(normalizedMaterialId).Base,
            TileDefIds.Mud => ResolveMudPalette(normalizedMaterialId).Base,
            TileDefIds.Snow => ResolveSnowPalette(normalizedMaterialId).Base,
            TileDefIds.Soil => ResolveSoilPalette(normalizedMaterialId).Base,
            TileDefIds.StoneFloor => ResolveStoneFloorPalette(normalizedMaterialId).Base,
            TileDefIds.StoneWall => ResolveStoneWallPalette(normalizedMaterialId).Base,
            TileDefIds.SoilWall => new Color(0.42f, 0.30f, 0.19f),
            TileDefIds.Empty => new Color(0.07f, 0.09f, 0.14f),
            _ => new Color(0.55f, 0.62f, 0.70f),
        };
    }

    private static string? NormalizeMaterialId(string? materialId)
        => string.IsNullOrWhiteSpace(materialId) ? null : materialId.Trim().ToLowerInvariant();

    private static Texture2D MakePlantOverlay(PlantPalette palette, byte growthStage, byte yieldLevel, byte seedLevel)
    {
        var image = NewImage();
        var (stem, leaf, accent, outline, canopy) = palette;

        if (canopy)
        {
            DrawCanopyOverlay(image, growthStage, yieldLevel, accent, leaf);
            return CreateOutlinedTexture(image, outline);
        }

        if (growthStage == 0 || seedLevel > 0)
        {
            FillRect(image, new Rect2I(20, 48, 6, 4), accent);
            FillRect(image, new Rect2I(30, 50, 4, 3), accent.Darkened(0.10f));
            FillRect(image, new Rect2I(38, 47, 5, 4), accent.Lightened(0.08f));
            return CreateOutlinedTexture(image, outline);
        }

        var stalkHeight = growthStage >= 3 ? 22 : growthStage == 2 ? 16 : 10;
        var stalkBottom = 52;
        var stalkWidth = growthStage >= 3 ? 4 : 3;
        for (var i = 0; i < 3; i++)
        {
            var x = 22 + (i * 8);
            FillRect(image, new Rect2I(x, stalkBottom - stalkHeight, stalkWidth, stalkHeight), stem);
        }

        if (growthStage >= 1)
        {
            FillRect(image, new Rect2I(16, 34, 14, 8), leaf);
            FillRect(image, new Rect2I(30, 30, 16, 10), leaf.Lightened(0.06f));
        }

        if (growthStage >= 2)
        {
            FillRect(image, new Rect2I(18, 24, 12, 8), leaf.Darkened(0.08f));
            FillRect(image, new Rect2I(32, 20, 14, 10), leaf);
        }

        if (growthStage >= 3)
        {
            FillRect(image, new Rect2I(20, 14, 10, 8), leaf.Lightened(0.05f));
            FillRect(image, new Rect2I(34, 12, 10, 8), leaf);
        }

        if (yieldLevel > 0)
        {
            FillRect(image, new Rect2I(22, 26, 4, 4), accent);
            FillRect(image, new Rect2I(28, 20, 4, 4), accent.Lightened(0.08f));
            FillRect(image, new Rect2I(36, 24, 4, 4), accent);
        }

        return CreateOutlinedTexture(image, outline);
    }

    private static PlantPalette ResolvePlantPaletteOrFallback(string plantDefId)
        => TryResolvePlantPalette(plantDefId, out var palette)
            ? palette
            : new PlantPalette(
                new Color(0.24f, 0.40f, 0.16f),
                new Color(0.34f, 0.62f, 0.24f),
                new Color(0.88f, 0.72f, 0.32f),
                new Color(0.08f, 0.16f, 0.08f, 0.95f),
                false);

    private static bool TryResolvePlantPalette(string plantDefId, out PlantPalette palette)
    {
        palette = plantDefId switch
        {
            "berry_bush" => new PlantPalette(new Color(0.22f, 0.40f, 0.14f), new Color(0.28f, 0.58f, 0.24f), new Color(0.74f, 0.18f, 0.42f), new Color(0.08f, 0.18f, 0.09f, 0.95f), false),
            "sunroot" => new PlantPalette(new Color(0.38f, 0.30f, 0.12f), new Color(0.38f, 0.68f, 0.22f), new Color(0.94f, 0.78f, 0.22f), new Color(0.16f, 0.20f, 0.08f, 0.95f), false),
            "stone_tuber" => new PlantPalette(new Color(0.36f, 0.30f, 0.16f), new Color(0.44f, 0.60f, 0.32f), new Color(0.66f, 0.60f, 0.46f), new Color(0.13f, 0.16f, 0.09f, 0.95f), false),
            "marsh_reed" => new PlantPalette(new Color(0.26f, 0.42f, 0.18f), new Color(0.36f, 0.72f, 0.28f), new Color(0.86f, 0.72f, 0.38f), new Color(0.07f, 0.16f, 0.08f, 0.95f), false),
            "apple_canopy" => new PlantPalette(new Color(0.22f, 0.34f, 0.14f), new Color(0.28f, 0.62f, 0.22f), new Color(0.88f, 0.16f, 0.12f), new Color(0.08f, 0.16f, 0.08f, 0.95f), true),
            "fig_canopy" => new PlantPalette(new Color(0.24f, 0.34f, 0.18f), new Color(0.38f, 0.62f, 0.28f), new Color(0.62f, 0.22f, 0.48f), new Color(0.08f, 0.16f, 0.08f, 0.95f), true),
            _ => default,
        };

        return palette != default;
    }

    private static void DrawCanopyOverlay(Image image, byte growthStage, byte yieldLevel, Color accent, Color leaf)
    {
        if (growthStage <= 0)
            return;

        FillRect(image, new Rect2I(18, 10, 10, 6), leaf.Darkened(0.08f));
        FillRect(image, new Rect2I(30, 8, 14, 8), leaf);
        FillRect(image, new Rect2I(20, 20, 24, 10), leaf.Lightened(0.05f));

        if (growthStage >= 2)
        {
            FillRect(image, new Rect2I(14, 18, 10, 8), leaf);
            FillRect(image, new Rect2I(40, 18, 10, 8), leaf.Darkened(0.05f));
        }

        if (yieldLevel > 0)
        {
            FillRect(image, new Rect2I(20, 18, 4, 4), accent);
            FillRect(image, new Rect2I(28, 14, 4, 4), accent.Lightened(0.10f));
            FillRect(image, new Rect2I(34, 20, 4, 4), accent);
            FillRect(image, new Rect2I(42, 16, 4, 4), accent.Lightened(0.06f));
        }
    }

    private static Texture2D MakeRockWallForMaterial(string? materialId)
    {
        var palette = ResolveStoneWallPalette(materialId);
        return MakeRockWall(palette.Base, palette.Shadow, palette.Highlight);
    }

    private static Texture2D MakeStoneFloorPalette(string? materialId)
    {
        var palette = ResolveStoneFloorPalette(materialId);
        return MakeStoneFloor(palette.Base, palette.Shadow, palette.Highlight);
    }

    private static Texture2D MakeGrassFloorPalette()
    {
        var grass = ResolveGrassPalette();
        var soil = ResolveSoilPalette(null);
        return MakeGrassFloor(grass.Base, grass.Shadow, grass.Highlight, soil.Base);
    }

    private static Texture2D MakeSoilFloorPalette(string? materialId)
    {
        var palette = ResolveSoilPalette(materialId);
        return MakeSoilFloor(palette.Base, palette.Shadow, palette.Highlight);
    }

    private static Texture2D MakeSandFloorPalette(string? materialId)
    {
        var palette = ResolveSandPalette(materialId);
        return MakeSandFloor(palette.Base, palette.Shadow, palette.Highlight);
    }

    private static Texture2D MakeMudFloorPalette(string? materialId)
    {
        var palette = ResolveMudPalette(materialId);
        return MakeMudFloor(palette.Base, palette.Shadow, palette.Highlight);
    }

    private static Texture2D MakeSnowFloorPalette(string? materialId)
    {
        var palette = ResolveSnowPalette(materialId);
        return MakeSnowFloor(palette.Base, palette.Shadow, palette.Highlight);
    }

    private static TilePalette ResolveStoneFloorPalette(string? materialId) => materialId switch
    {
        "limestone" => new TilePalette(new Color(0.72f, 0.70f, 0.61f), new Color(0.53f, 0.50f, 0.43f), new Color(0.84f, 0.82f, 0.74f)),
        "sandstone" => new TilePalette(new Color(0.67f, 0.57f, 0.40f), new Color(0.49f, 0.39f, 0.26f), new Color(0.80f, 0.70f, 0.52f)),
        "basalt" => new TilePalette(new Color(0.33f, 0.33f, 0.37f), new Color(0.22f, 0.22f, 0.26f), new Color(0.48f, 0.48f, 0.54f)),
        "shale" => new TilePalette(new Color(0.43f, 0.40f, 0.37f), new Color(0.29f, 0.26f, 0.23f), new Color(0.56f, 0.52f, 0.49f)),
        "slate" => new TilePalette(new Color(0.39f, 0.42f, 0.46f), new Color(0.26f, 0.28f, 0.32f), new Color(0.54f, 0.58f, 0.64f)),
        "marble" => new TilePalette(new Color(0.79f, 0.79f, 0.82f), new Color(0.61f, 0.61f, 0.64f), new Color(0.91f, 0.91f, 0.94f)),
        _ => new TilePalette(new Color(0.43f, 0.45f, 0.49f), new Color(0.34f, 0.36f, 0.40f), new Color(0.62f, 0.64f, 0.69f)),
    };

    private static TilePalette ResolveGrassPalette()
        => new(new Color(0.24f, 0.58f, 0.24f), new Color(0.14f, 0.36f, 0.14f), new Color(0.34f, 0.62f, 0.30f));

    private static TilePalette ResolveStoneWallPalette(string? materialId) => materialId switch
    {
        "limestone" => new TilePalette(new Color(0.74f, 0.72f, 0.63f), new Color(0.52f, 0.50f, 0.42f), new Color(0.86f, 0.84f, 0.76f)),
        "sandstone" => new TilePalette(new Color(0.70f, 0.60f, 0.42f), new Color(0.50f, 0.40f, 0.27f), new Color(0.83f, 0.74f, 0.55f)),
        "basalt" => new TilePalette(new Color(0.34f, 0.34f, 0.38f), new Color(0.20f, 0.20f, 0.24f), new Color(0.48f, 0.48f, 0.54f)),
        "shale" => new TilePalette(new Color(0.44f, 0.42f, 0.40f), new Color(0.28f, 0.26f, 0.24f), new Color(0.58f, 0.56f, 0.52f)),
        "slate" => new TilePalette(new Color(0.38f, 0.41f, 0.45f), new Color(0.22f, 0.25f, 0.29f), new Color(0.52f, 0.56f, 0.62f)),
        "marble" => new TilePalette(new Color(0.80f, 0.80f, 0.82f), new Color(0.60f, 0.60f, 0.63f), new Color(0.90f, 0.90f, 0.92f)),
        _ => new TilePalette(new Color(0.46f, 0.48f, 0.54f), new Color(0.28f, 0.30f, 0.36f), new Color(0.60f, 0.62f, 0.68f)),
    };

    private static TilePalette ResolveSoilPalette(string? materialId) => materialId switch
    {
        "peat" => new TilePalette(new Color(0.22f, 0.17f, 0.13f), new Color(0.15f, 0.11f, 0.08f), new Color(0.33f, 0.25f, 0.19f)),
        "loam" => new TilePalette(new Color(0.56f, 0.39f, 0.22f), new Color(0.42f, 0.28f, 0.15f), new Color(0.69f, 0.50f, 0.31f)),
        _ => new TilePalette(new Color(0.49f, 0.34f, 0.18f), new Color(0.39f, 0.26f, 0.13f), new Color(0.61f, 0.44f, 0.27f)),
    };

    private static TilePalette ResolveSandPalette(string? materialId) => materialId switch
    {
        "dune_sand" => new TilePalette(new Color(0.84f, 0.76f, 0.53f), new Color(0.70f, 0.61f, 0.39f), new Color(0.93f, 0.87f, 0.66f)),
        "red_sand" => new TilePalette(new Color(0.78f, 0.53f, 0.36f), new Color(0.61f, 0.39f, 0.25f), new Color(0.89f, 0.67f, 0.48f)),
        _ => new TilePalette(new Color(0.78f, 0.69f, 0.45f), new Color(0.66f, 0.57f, 0.35f), new Color(0.89f, 0.82f, 0.60f)),
    };

    private static TilePalette ResolveMudPalette(string? materialId) => materialId switch
    {
        "clay_mud" => new TilePalette(new Color(0.50f, 0.28f, 0.20f), new Color(0.38f, 0.18f, 0.13f), new Color(0.66f, 0.40f, 0.30f)),
        "silt_mud" => new TilePalette(new Color(0.46f, 0.36f, 0.28f), new Color(0.34f, 0.26f, 0.20f), new Color(0.60f, 0.48f, 0.39f)),
        _ => new TilePalette(new Color(0.36f, 0.27f, 0.17f), new Color(0.27f, 0.19f, 0.12f), new Color(0.51f, 0.39f, 0.26f)),
    };

    private static TilePalette ResolveSnowPalette(string? materialId) => materialId switch
    {
        "ice" => new TilePalette(new Color(0.76f, 0.88f, 0.97f), new Color(0.60f, 0.73f, 0.86f), new Color(0.91f, 0.97f, 1.00f)),
        "frost" => new TilePalette(new Color(0.90f, 0.94f, 0.99f), new Color(0.76f, 0.83f, 0.91f), new Color(0.98f, 0.99f, 1.00f)),
        _ => new TilePalette(new Color(0.86f, 0.90f, 0.96f), new Color(0.73f, 0.78f, 0.87f), new Color(0.95f, 0.97f, 1.00f)),
    };

    public static Texture2D GetWorldTile(string macroBiomeId)
        => GetWorldTile(macroBiomeId, forestLevel: 0, mountainLevel: 0);

    public static Texture2D GetWorldTile(string macroBiomeId, byte forestLevel, byte mountainLevel)
    {
        var forest = (byte)Math.Clamp((int)forestLevel, 0, 2);
        var mountain = (byte)Math.Clamp((int)mountainLevel, 0, 2);
        var key = $"world:{macroBiomeId}:f{forest}:m{mountain}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var baseColor = ResolveWorldBaseColor(macroBiomeId);
        return Cache[key] = MakeWorldBiomeTile(baseColor, forest, mountain);
    }

    public static Texture2D GetRegionTile(string biomeVariantId)
        => GetRegionTile(
            biomeVariantId,
            vegetationDensity: 0.5f,
            slopeNorm: 0.25f,
            groundwater: 0.5f,
            hasRiver: false,
            hasLake: false,
            riverEdges: RegionRiverEdges.None,
            hasRoad: false,
            roadEdges: RegionRoadEdges.None,
            temperatureBand: 0.5f,
            moistureBand: 0.5f,
            flowAccumulationBand: 0f,
            parentMacroBiomeId: MacroBiomeIds.TemperatePlains,
            surfaceClassId: RegionSurfaceClassIds.Grass,
            patternVariant: 0);

    public static Texture2D GetRegionTile(
        string biomeVariantId,
        float vegetationDensity,
        float slopeNorm,
        float groundwater,
        bool hasRiver,
        bool hasLake,
        RegionRiverEdges riverEdges,
        bool hasRoad,
        RegionRoadEdges roadEdges,
        float temperatureBand,
        float moistureBand,
        float flowAccumulationBand,
        string parentMacroBiomeId,
        string? surfaceClassId = null,
        byte patternVariant = 0)
    {
        var vegetationBand = Quantize01(vegetationDensity, 8);
        var slopeBand = Quantize01(slopeNorm, 8);
        var wetBand = Quantize01(groundwater, 8);
        var temperature = Quantize01(temperatureBand, 8);
        var moisture = Quantize01(moistureBand, 8);
        var flow = Quantize01(flowAccumulationBand, 8);
        var forestIconLevel = ResolveRegionForestIconLevel(
            biomeVariantId,
            parentMacroBiomeId,
            vegetationDensity,
            moistureBand,
            flowAccumulationBand);
        var mountainIconLevel = ResolveRegionMountainIconLevel(
            biomeVariantId,
            parentMacroBiomeId,
            slopeNorm);
        var riverFlag = hasRiver ? 1 : 0;
        var lakeFlag = hasLake ? 1 : 0;
        var roadFlag = hasRoad ? 1 : 0;
        var normalizedSurfaceClassId = string.IsNullOrWhiteSpace(surfaceClassId)
            ? "-"
            : surfaceClassId.Trim().ToLowerInvariant();
        var key = $"region:{biomeVariantId}:v{vegetationBand}:s{slopeBand}:w{wetBand}:t{temperature}:m{moisture}:f{flow}:fi{forestIconLevel}:mi{mountainIconLevel}:r{riverFlag}:{(byte)riverEdges}:l{lakeFlag}:rd{roadFlag}:{(byte)roadEdges}:p:{parentMacroBiomeId}:g:{normalizedSurfaceClassId}:pv{patternVariant}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var baseColor = ResolveRegionBaseColor(biomeVariantId);
        var parentColor = ResolveWorldBaseColor(parentMacroBiomeId);
        return Cache[key] = MakeRegionBiomeTile(
            normalizedSurfaceClassId,
            biomeVariantId,
            parentMacroBiomeId,
            baseColor,
            parentColor,
            vegetationDensity,
            slopeNorm,
            groundwater,
            vegetationBand,
            slopeBand,
            wetBand,
            temperatureBand,
            moistureBand,
            flowAccumulationBand,
            forestIconLevel,
            mountainIconLevel,
            patternVariant,
            hasRiver,
            riverEdges,
            hasLake,
            hasRoad,
            roadEdges);
    }

    public static Texture2D GetEntity(string id)
    {
        if (TryGetEntity(id, out var texture) && texture is not null)
            return texture;

        if (Strict2DArt)
            throw CreateMissingArtException("entity", id);

        var key = $"entity:{id}";
        var profileId = ClientContentQueries.ResolveCreatureProceduralProfileId(id);
        StrictResolvedKeys.Remove(key);
        return Cache[key] = ResolveCreatureTexture(id, profileId);
    }

    public static bool TryGetEntity(string id, out Texture2D? texture)
    {
        var key = $"entity:{id}";
        if (StrictResolvedKeys.Contains(key) && Cache.TryGetValue(key, out var cached))
        {
            texture = cached;
            return true;
        }

        if (UseSprites && SpriteRegistry.TryGetEntity(id, out var sprite) && sprite is not null)
        {
            texture = Cache[key] = sprite;
            StrictResolvedKeys.Add(key);
            return true;
        }

        var profileId = ClientContentQueries.ResolveCreatureProceduralProfileId(id);
        if (!TryResolveCreatureTexture(id, profileId, out texture) || texture is null)
            return false;

        Cache[key] = texture;
        StrictResolvedKeys.Add(key);
        return true;
    }

    private static Texture2D ResolveCreatureTexture(string id, string? profileId)
    {
        if (TryResolveCreatureTexture(id, profileId, out var texture) && texture is not null)
            return texture;

        return MakeCreatureFromId(id);
    }

    private static bool TryResolveCreatureTexture(string id, string? profileId, out Texture2D? texture)
    {
        texture = profileId switch
        {
            ContentCreatureVisualProfileIds.Dwarf => GetDwarf(DefaultDwarfAppearance),
            ContentCreatureVisualProfileIds.Goblin => MakeGoblin(),
            ContentCreatureVisualProfileIds.Troll => MakeTroll(),
            ContentCreatureVisualProfileIds.Elk => MakeElk(),
            ContentCreatureVisualProfileIds.GiantCarp => MakeGiantCarp(),
            ContentCreatureVisualProfileIds.Cat => MakeCat(),
            ContentCreatureVisualProfileIds.Dog => MakeDog(),
            _ => null,
        };

        if (texture is not null)
            return true;

        return profileId is null && TryResolveLegacyCreatureTexture(id, out texture);
    }

    private static bool TryResolveLegacyCreatureTexture(string id, out Texture2D? texture)
    {
        texture = id switch
        {
            DefIds.Dwarf => GetDwarf(DefaultDwarfAppearance),
            _ => null,
        };

        return texture is not null;
    }

    public static Texture2D GetItem(string itemDefId, string? materialId = null)
    {
        if (TryGetItem(itemDefId, materialId, out var texture) && texture is not null)
            return texture;

        var normalizedMaterialId = NormalizeMaterialId(materialId);
        if (Strict2DArt)
            throw CreateMissingArtException("item", itemDefId, normalizedMaterialId);

        var key = normalizedMaterialId is null ? $"item:{itemDefId}" : $"item:{itemDefId}:{normalizedMaterialId}";
        StrictResolvedKeys.Remove(key);
        return Cache[key] = MakeCrate(new Color(0.64f, 0.64f, 0.64f));
    }

    public static bool TryGetItem(string itemDefId, string? materialId, out Texture2D? texture)
    {
        var normalizedMaterialId = NormalizeMaterialId(materialId);
        var key = normalizedMaterialId is null ? $"item:{itemDefId}" : $"item:{itemDefId}:{normalizedMaterialId}";
        if (StrictResolvedKeys.Contains(key) && Cache.TryGetValue(key, out var cached))
        {
            texture = cached;
            return true;
        }

        if (UseSprites && SpriteRegistry.TryGetItem(itemDefId, out var sprite) && sprite is not null)
        {
            texture = Cache[key] = sprite;
            StrictResolvedKeys.Add(key);
            return true;
        }

        if (TryMakeDerivedResourceItem(itemDefId, normalizedMaterialId, out var derived))
        {
            texture = Cache[key] = derived;
            StrictResolvedKeys.Add(key);
            return true;
        }

        texture = itemDefId switch
        {
            ItemDefIds.Meal => MakeMeal(),
            ItemDefIds.Drink => MakeDrink(),
            ItemDefIds.PlantMatter => MakePlantBundle(),
            ItemDefIds.BerryCluster => MakeBerryCluster(),
            ItemDefIds.SunrootBulb => MakeSunrootBulb(),
            ItemDefIds.StoneTuber => MakeStoneTuber(),
            ItemDefIds.MarshReedShoot => MakeMarshReedShoot(),
            ItemDefIds.Apple => MakeApple(),
            ItemDefIds.Fig => MakeFig(),
            ItemDefIds.Leather => MakeHide(),
            ItemDefIds.Cloth => MakeCloth(),
            ItemDefIds.Bone => MakeBone(),
            ItemDefIds.Corpse => MakeCorpse(),
            ItemDefIds.Bed => MakeBed(),
            ItemDefIds.Table => MakeTable(),
            ItemDefIds.Chair => MakeChair(),
            ItemDefIds.Barrel => MakeBarrel(),
            ItemDefIds.Bucket => MakeBucket(),
            ItemDefIds.Box => MakeBox(),
            _ => null,
        };

        if (texture is null)
            return false;

        Cache[key] = texture;
        StrictResolvedKeys.Add(key);
        return true;
    }

    public static Texture2D GetBuilding(string buildingDefId)
    {
        if (TryGetBuilding(buildingDefId, out var texture) && texture is not null)
            return texture;

        if (Strict2DArt)
            throw CreateMissingArtException("building", buildingDefId);

        var key = $"building:{buildingDefId}";
        StrictResolvedKeys.Remove(key);
        return Cache[key] = MakeWorkshop(new Color(0.78f, 0.55f, 0.22f), new Color(0.40f, 0.26f, 0.10f), new Color(0.90f, 0.86f, 0.72f));
    }

    public static bool TryGetBuilding(string buildingDefId, out Texture2D? texture)
    {
        var key = $"building:{buildingDefId}";
        if (StrictResolvedKeys.Contains(key) && Cache.TryGetValue(key, out var cached))
        {
            texture = cached;
            return true;
        }

        if (UseSprites && SpriteRegistry.TryGetBuilding(buildingDefId, out var sprite) && sprite is not null)
        {
            texture = Cache[key] = sprite;
            StrictResolvedKeys.Add(key);
            return true;
        }

        texture = buildingDefId switch
        {
            BuildingDefIds.CarpenterWorkshop => MakeWorkshop(new Color(0.64f, 0.43f, 0.22f), new Color(0.34f, 0.20f, 0.10f), new Color(0.82f, 0.73f, 0.58f)),
            BuildingDefIds.Smelter => MakeWorkshop(new Color(0.46f, 0.48f, 0.52f), new Color(0.22f, 0.24f, 0.28f), new Color(0.95f, 0.54f, 0.16f)),
            BuildingDefIds.Kitchen => MakeWorkshop(new Color(0.73f, 0.68f, 0.54f), new Color(0.42f, 0.35f, 0.20f), new Color(0.47f, 0.72f, 0.31f)),
            BuildingDefIds.Still => MakeWorkshop(new Color(0.58f, 0.54f, 0.62f), new Color(0.30f, 0.26f, 0.34f), new Color(0.34f, 0.72f, 0.86f)),
            BuildingDefIds.Bed => MakeBed(),
            BuildingDefIds.Table => MakeTable(),
            BuildingDefIds.Chair => MakeChair(),
            _ => null,
        };

        if (texture is null)
            return false;

        Cache[key] = texture;
        StrictResolvedKeys.Add(key);
        return true;
    }

    private static bool ParseFeatureFlag(string envVar)
    {
        var rawValue = global::System.Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        rawValue = rawValue.Trim();
        if (bool.TryParse(rawValue, out var boolValue))
            return boolValue;

        return rawValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException CreateMissingArtException(string category, string id, string? materialId = null)
    {
        var materialSuffix = string.IsNullOrWhiteSpace(materialId) ? string.Empty : $" (material '{materialId}')";
        return new InvalidOperationException(
            $"[PixelArtFactory] Missing 2D {category} art for '{id}'{materialSuffix}. Add a sprite mapping or explicit PixelArtFactory resolver, or disable {Strict2DArtEnvVar}.");
    }

    private static Texture2D MakeFlatTile(Color color)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), color);
        Outline(image, color.Darkened(0.2f));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeRegionBiomeTile(
        string? surfaceClassId,
        string biomeVariantId,
        string parentMacroBiomeId,
        Color baseColor,
        Color parentColor,
        float vegetationDensity,
        float slopeNorm,
        float groundwater,
        byte vegetationBand,
        byte slopeBand,
        byte wetBand,
        float temperatureBand,
        float moistureBand,
        float flowAccumulationBand,
        byte forestIconLevel,
        byte mountainIconLevel,
        byte patternVariant,
        bool hasRiver,
        RegionRiverEdges riverEdges,
        bool hasLake,
        bool hasRoad,
        RegionRoadEdges roadEdges)
    {
        var image = NewImage();
        var blendedBase = new Color(
            Mathf.Lerp(baseColor.R, parentColor.R, 0.22f),
            Mathf.Lerp(baseColor.G, parentColor.G, 0.22f),
            Mathf.Lerp(baseColor.B, parentColor.B, 0.22f));
        var groundClass = ResolveRegionGroundClass(surfaceClassId, biomeVariantId, parentMacroBiomeId);
        var groundPalette = ResolveRegionGroundPalette(groundClass);
        var surfaceLedBase = new Color(
            Mathf.Lerp(blendedBase.R, groundPalette.Base.R, groundClass == RegionGroundClass.Grass ? 0.34f : 0.24f),
            Mathf.Lerp(blendedBase.G, groundPalette.Base.G, groundClass == RegionGroundClass.Grass ? 0.34f : 0.24f),
            Mathf.Lerp(blendedBase.B, groundPalette.Base.B, groundClass == RegionGroundClass.Grass ? 0.34f : 0.24f));
        FillRect(image, new Rect2I(0, 0, Size, Size), surfaceLedBase);
        DrawRegionGroundTexture(image, groundClass, surfaceLedBase, 1.14f);
        ApplyRegionReliefGradient(image, slopeNorm, groundwater, moistureBand);
        DrawRegionVegetationPreview(
            image,
            groundClass,
            vegetationDensity,
            moistureBand,
            groundwater,
            flowAccumulationBand,
            forestIconLevel,
            patternVariant);
        DrawRegionReliefPreview(
            image,
            groundClass,
            slopeNorm,
            mountainIconLevel,
            patternVariant);

        if (wetBand > 0)
        {
            BlendRect(
                image,
                new Rect2I(4, 4, Size - 8, Size - 8),
                new Color(0.16f, 0.36f, 0.62f),
                0.03f + (wetBand * 0.016f));
        }

        var clampedTemperature = Math.Clamp(temperatureBand, 0f, 1f);
        var warmStrength = MathF.Max(0f, clampedTemperature - 0.50f) * 0.10f;
        var coolStrength = MathF.Max(0f, 0.50f - clampedTemperature) * 0.12f;
        if (warmStrength > 0f)
            BlendRect(image, new Rect2I(0, 0, Size, Size), new Color(0.88f, 0.42f, 0.16f), warmStrength);
        if (coolStrength > 0f)
            BlendRect(image, new Rect2I(0, 0, Size, Size), new Color(0.22f, 0.42f, 0.80f), coolStrength);

        var clampedMoisture = Math.Clamp(moistureBand, 0f, 1f);
        if (clampedMoisture >= 0.62f)
        {
            var humidStrength = (clampedMoisture - 0.62f) * 0.24f;
            BlendRect(image, new Rect2I(6, 6, Size - 12, Size - 12), new Color(0.14f, 0.44f, 0.28f), humidStrength);
        }
        else if (clampedMoisture <= 0.36f)
        {
            var dryStrength = (0.36f - clampedMoisture) * 0.18f;
            BlendRect(image, new Rect2I(6, 6, Size - 12, Size - 12), new Color(0.86f, 0.72f, 0.42f), dryStrength);
        }

        if (hasLake)
            DrawRegionLakePreview(image, patternVariant);

        if (hasRiver)
        {
            var channelWidth = Math.Max(3, (int)MathF.Round(2f + (Math.Clamp(flowAccumulationBand, 0f, 1f) * 4f)));
            DrawRegionRiverNetwork(image, riverEdges, channelWidth, patternVariant);
        }

        if (hasRoad)
            DrawRegionRoadNetwork(image, roadEdges, patternVariant);

        Outline(image, surfaceLedBase.Darkened(0.26f));
        return ImageTexture.CreateFromImage(image);
    }

    private static byte ResolveRegionForestIconLevel(
        string biomeVariantId,
        string parentMacroBiomeId,
        float vegetationDensity,
        float moistureBand,
        float flowAccumulationBand)
    {
        if (RegionBiomeVariantIds.IsOceanVariant(biomeVariantId) || MacroBiomeIds.IsOcean(parentMacroBiomeId))
            return 0;

        var score = Math.Clamp(
            (Math.Clamp(vegetationDensity, 0f, 1f) * 0.70f) +
            (Math.Clamp(moistureBand, 0f, 1f) * 0.20f) +
            (Math.Clamp(flowAccumulationBand, 0f, 1f) * 0.10f),
            0f,
            1f);

        if (RegionBiomeVariantIds.IsMarshVariant(biomeVariantId))
            score = MathF.Min(score, 0.64f);
        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Desert, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentMacroBiomeId, MacroBiomeIds.IcePlains, StringComparison.OrdinalIgnoreCase))
        {
            score *= 0.58f;
        }
        else if (string.Equals(parentMacroBiomeId, MacroBiomeIds.WindsweptSteppe, StringComparison.OrdinalIgnoreCase))
        {
            score *= 0.72f;
        }

        if (score >= 0.84f)
            return 4;
        if (score >= 0.68f)
            return 3;
        if (score >= 0.50f)
            return 2;
        if (score >= 0.34f)
            return 1;
        return 0;
    }

    private static byte ResolveRegionMountainIconLevel(
        string biomeVariantId,
        string parentMacroBiomeId,
        float slopeNorm)
    {
        if (RegionBiomeVariantIds.IsOceanVariant(biomeVariantId) || MacroBiomeIds.IsOcean(parentMacroBiomeId))
            return 0;

        var score = Math.Clamp(slopeNorm, 0f, 1f);
        if (RegionBiomeVariantIds.IsHighlandVariant(biomeVariantId) || RegionBiomeVariantIds.IsRockyVariant(biomeVariantId))
            score = Math.Clamp(score + 0.16f, 0f, 1f);
        if (string.Equals(parentMacroBiomeId, MacroBiomeIds.Highland, StringComparison.OrdinalIgnoreCase))
            score = Math.Clamp(score + 0.08f, 0f, 1f);

        if (score >= 0.88f)
            return 4;
        if (score >= 0.74f)
            return 3;
        if (score >= 0.58f)
            return 2;
        if (score >= 0.44f)
            return 1;
        return 0;
    }

    private static RegionGroundClass ResolveRegionGroundClass(
        string? surfaceClassId,
        string biomeVariantId,
        string parentMacroBiomeId)
    {
        var resolvedSurfaceClassId = RegionSurfaceResolver.ResolveAnchorSurfaceClassId(
            surfaceClassId,
            biomeVariantId,
            parentMacroBiomeId);

        return resolvedSurfaceClassId switch
        {
            RegionSurfaceClassIds.Sand => RegionGroundClass.Sand,
            RegionSurfaceClassIds.Mud => RegionGroundClass.Mud,
            RegionSurfaceClassIds.Snow => RegionGroundClass.Snow,
            RegionSurfaceClassIds.Stone => RegionGroundClass.Stone,
            RegionSurfaceClassIds.Soil => RegionGroundClass.Soil,
            _ => RegionGroundClass.Grass,
        };
    }

    private static void DrawRegionGroundTexture(Image image, RegionGroundClass groundClass, Color baseColor, float intensity)
    {
        var clampedIntensity = Math.Clamp(intensity, 0f, 1f);
        if (clampedIntensity <= 0f)
            return;

        var palette = ResolveRegionGroundPalette(groundClass);
        BlendRect(image, new Rect2I(0, 0, Size, Size), palette.Base, 0.20f * clampedIntensity);
        switch (groundClass)
        {
            case RegionGroundClass.Sand:
            {
                var duneShadow = palette.Shadow;
                var duneHighlight = palette.Highlight;
                for (var y = 8; y < Size; y += 12)
                {
                    var x = 4 + ((y * 7) % 16);
                    BlendRect(image, new Rect2I(x, y, 18, 2), duneShadow, 0.20f * clampedIntensity);
                    BlendRect(image, new Rect2I(x + 6, y - 1, 10, 1), duneHighlight, 0.18f * clampedIntensity);
                }

                break;
            }
            case RegionGroundClass.Mud:
            {
                DrawMudPatternBlend(image, palette.Base, palette.Shadow, palette.Highlight, clampedIntensity);

                break;
            }
            case RegionGroundClass.Snow:
            {
                var snowShadow = palette.Shadow;
                var sparkle = palette.Highlight.Lightened(0.05f);
                for (var y = 5; y < Size; y += 11)
                for (var x = 5; x < Size; x += 11)
                {
                    BlendRect(image, new Rect2I(x, y, 2, 2), snowShadow, 0.18f * clampedIntensity);
                    if (((x + y) / 11) % 3 == 0)
                        BlendRect(image, new Rect2I(x + 2, y + 1, 1, 1), sparkle, 0.28f * clampedIntensity);
                }

                break;
            }
            case RegionGroundClass.Stone:
            {
                var crack = palette.Shadow.Darkened(0.08f);
                var edge = palette.Highlight;
                for (var i = 6; i < Size - 6; i += 9)
                {
                    BlendRect(image, new Rect2I(i, 6 + (i % 8), 1, 14), crack, 0.22f * clampedIntensity);
                    BlendRect(image, new Rect2I(i + 1, 8 + (i % 7), 1, 10), edge, 0.16f * clampedIntensity);
                }

                break;
            }
            case RegionGroundClass.Soil:
            {
                var clump = palette.Shadow;
                for (var y = 6; y < Size; y += 10)
                for (var x = 6; x < Size; x += 10)
                {
                    if (((x + y) / 10) % 2 == 0)
                        BlendRect(image, new Rect2I(x, y, 4, 3), clump, 0.16f * clampedIntensity);
                }

                break;
            }
            case RegionGroundClass.Grass:
            default:
            {
                var blade = 
                    new Color(
                        Mathf.Lerp(baseColor.R, palette.Shadow.R, 0.38f),
                        Mathf.Lerp(baseColor.G, palette.Base.G, 0.42f),
                        Mathf.Lerp(baseColor.B, palette.Shadow.B, 0.30f));
                for (var y = 6; y < Size; y += 9)
                {
                    var x = 5 + ((y * 3) % 11);
                    BlendRect(image, new Rect2I(x, y, 2, 3), blade, 0.16f * clampedIntensity);
                }

                break;
            }
        }
    }

    private static TilePalette ResolveRegionGroundPalette(RegionGroundClass groundClass)
        => groundClass switch
        {
            RegionGroundClass.Sand => ResolveSandPalette(null),
            RegionGroundClass.Mud => ResolveMudPalette(null),
            RegionGroundClass.Snow => ResolveSnowPalette(null),
            RegionGroundClass.Stone => ResolveStoneFloorPalette(null),
            RegionGroundClass.Soil => ResolveSoilPalette(null),
            _ => ResolveGrassPalette(),
        };

    private static byte Quantize01(float value, byte levels)
    {
        if (levels == 0)
            return 0;

        var clamped = Math.Clamp(value, 0f, 1f);
        return (byte)Math.Clamp((int)MathF.Round(clamped * levels), 0, levels);
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static Color ResolveRegionBaseColor(string biomeVariantId)
    {
        if (RegionBiomeVariantIds.IsConiferVariant(biomeVariantId))
            return new Color(0.16f, 0.38f, 0.18f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.ForestedFoothills, StringComparison.OrdinalIgnoreCase))
            return new Color(0.30f, 0.46f, 0.24f);
        if (RegionBiomeVariantIds.IsHighlandVariant(biomeVariantId) ||
            string.Equals(biomeVariantId, RegionBiomeVariantIds.RockyHighland, StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.56f, 0.58f, 0.64f);
        }
        if (RegionBiomeVariantIds.IsMarshVariant(biomeVariantId) ||
            string.Equals(biomeVariantId, RegionBiomeVariantIds.BoggyFen, StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.30f, 0.47f, 0.40f);
        }
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.TropicalCanopy, StringComparison.OrdinalIgnoreCase))
            return new Color(0.09f, 0.46f, 0.22f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.TropicalLowland, StringComparison.OrdinalIgnoreCase))
            return new Color(0.19f, 0.52f, 0.30f);
        if (RegionBiomeVariantIds.IsSteppeVariant(biomeVariantId) ||
            string.Equals(biomeVariantId, RegionBiomeVariantIds.SparseSteppe, StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.72f, 0.65f, 0.40f);
        }
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.AridBadlands, StringComparison.OrdinalIgnoreCase))
            return new Color(0.68f, 0.52f, 0.34f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.PolarTundra, StringComparison.OrdinalIgnoreCase))
            return new Color(0.56f, 0.61f, 0.66f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.GlacialField, StringComparison.OrdinalIgnoreCase))
            return new Color(0.82f, 0.89f, 0.95f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.TemperateWoodland, StringComparison.OrdinalIgnoreCase))
            return new Color(0.31f, 0.56f, 0.31f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.MeadowPlain, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(biomeVariantId, RegionBiomeVariantIds.TemperatePlainsOpen, StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.44f, 0.63f, 0.35f);
        }
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.RiverValley, StringComparison.OrdinalIgnoreCase))
            return new Color(0.36f, 0.58f, 0.38f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.CoastalShallows, StringComparison.OrdinalIgnoreCase))
            return new Color(0.24f, 0.46f, 0.72f);
        if (string.Equals(biomeVariantId, RegionBiomeVariantIds.OpenOcean, StringComparison.OrdinalIgnoreCase))
            return new Color(0.10f, 0.24f, 0.42f);

        return new Color(0.40f, 0.60f, 0.32f);
    }

    private static Color ResolveWorldBaseColor(string macroBiomeId)
    {
        return macroBiomeId switch
        {
            MacroBiomeIds.OceanDeep => new Color(0.05f, 0.20f, 0.42f),
            MacroBiomeIds.OceanShallow => new Color(0.15f, 0.42f, 0.72f),
            MacroBiomeIds.ConiferForest => new Color(0.08f, 0.28f, 0.13f),
            MacroBiomeIds.BorealForest => new Color(0.12f, 0.34f, 0.20f),
            MacroBiomeIds.Highland => new Color(0.52f, 0.54f, 0.60f),
            MacroBiomeIds.MistyMarsh => new Color(0.34f, 0.50f, 0.44f),
            MacroBiomeIds.WindsweptSteppe => new Color(0.73f, 0.66f, 0.40f),
            MacroBiomeIds.Savanna => new Color(0.80f, 0.67f, 0.30f),
            MacroBiomeIds.Desert => new Color(0.84f, 0.74f, 0.50f),
            MacroBiomeIds.Tundra => new Color(0.62f, 0.68f, 0.72f),
            MacroBiomeIds.IcePlains => new Color(0.80f, 0.87f, 0.95f),
            MacroBiomeIds.TropicalRainforest => new Color(0.08f, 0.42f, 0.24f),
            MacroBiomeIds.TemperatePlains => new Color(0.18f, 0.49f, 0.31f),
            _ => new Color(0.27f, 0.60f, 0.28f),
        };
    }

    private static Texture2D MakeWorldBiomeTile(Color baseColor, byte forestLevel, byte mountainLevel)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);
        Outline(image, baseColor.Darkened(0.2f));

        if (mountainLevel > 0)
            DrawWorldMountainIcon(image, baseColor, mountainLevel);
        if (forestLevel > 0)
            DrawWorldForestIcon(image, baseColor, forestLevel);

        return ImageTexture.CreateFromImage(image);
    }

    private static Color WithAlpha(Color color, float alpha)
        => new(color.R, color.G, color.B, Mathf.Clamp(alpha, 0f, 1f));

    private static void DrawWorldMountainIcon(Image image, Color baseColor, byte level)
    {
        var clamped = (byte)Math.Clamp((int)level, 1, 4);
        var mainHeight = clamped switch
        {
            1 => 11,
            2 => 15,
            3 => 18,
            _ => 21,
        };
        var shade = baseColor.Lightened(clamped >= 3 ? 0.48f : 0.38f);
        var shadow = baseColor.Lightened(0.22f);
        DrawTriangle(image, centerX: 32, baseY: 45, height: mainHeight, color: shade);
        if (clamped >= 2)
            DrawTriangle(image, centerX: 21, baseY: 47, height: 10 + ((clamped - 2) * 2), color: shadow);
        if (clamped >= 4)
            DrawTriangle(image, centerX: 43, baseY: 47, height: 10, color: shadow.Darkened(0.08f));
    }

    private static void DrawWorldForestIcon(Image image, Color baseColor, byte level)
    {
        var clamped = (byte)Math.Clamp((int)level, 1, 4);
        var canopy = new Color(
            Mathf.Lerp(baseColor.R, 0.08f, 0.65f),
            Mathf.Lerp(baseColor.G, 0.34f, 0.70f),
            Mathf.Lerp(baseColor.B, 0.10f, 0.75f));

        DrawDisk(image, centerX: 30, centerY: 40, radius: clamped >= 2 ? 5 : 4, color: canopy);
        if (clamped >= 2)
        {
            DrawDisk(image, centerX: 22, centerY: 43, radius: 4, color: canopy.Lightened(0.05f));
            DrawDisk(image, centerX: 38, centerY: 43, radius: 4, color: canopy.Darkened(0.05f));
        }
        if (clamped >= 3)
            DrawDisk(image, centerX: 30, centerY: 34, radius: 4, color: canopy.Lightened(0.10f));
        if (clamped >= 4)
            DrawDisk(image, centerX: 16, centerY: 44, radius: 3, color: canopy.Darkened(0.09f));
    }

    private static void ApplyRegionReliefGradient(Image image, float slopeNorm, float groundwater, float moistureBand)
    {
        var reliefStrength = Math.Clamp((slopeNorm * 0.78f) + ((1f - groundwater) * 0.10f), 0f, 1f);
        if (reliefStrength <= 0.04f)
            return;

        for (var y = 0; y < Size; y++)
        {
            var t = y / (float)Math.Max(1, Size - 1);
            var shadow = reliefStrength * t * 0.16f;
            var light = reliefStrength * (1f - t) * 0.11f;
            BlendRect(image, new Rect2I(0, y, Size, 1), new Color(0.16f, 0.20f, 0.28f), shadow);
            BlendRect(image, new Rect2I(0, y, Size, 1), new Color(0.94f, 0.96f, 0.99f), light);
        }

        var aridOverlay = Math.Clamp((1f - moistureBand) * 0.10f, 0f, 0.10f);
        if (aridOverlay > 0f)
            BlendRect(image, new Rect2I(6, 6, Size - 12, Size - 12), new Color(0.82f, 0.68f, 0.40f), aridOverlay);
    }

    private static void DrawRegionVegetationPreview(
        Image image,
        RegionGroundClass groundClass,
        float vegetationDensity,
        float moistureBand,
        float groundwater,
        float flowAccumulationBand,
        byte forestIconLevel,
        byte patternVariant)
    {
        var presence = Math.Clamp(
            (Math.Clamp(vegetationDensity, 0f, 1f) * 0.72f) +
            (Math.Clamp(moistureBand, 0f, 1f) * 0.16f) +
            (Math.Clamp(groundwater, 0f, 1f) * 0.07f) +
            (Math.Clamp(flowAccumulationBand, 0f, 1f) * 0.05f),
            0f,
            1f);

        presence *= groundClass switch
        {
            RegionGroundClass.Sand => 0.42f,
            RegionGroundClass.Snow => 0.48f,
            RegionGroundClass.Stone => 0.62f,
            RegionGroundClass.Mud => 0.70f,
            _ => 1f,
        };

        if (presence < 0.16f && forestIconLevel == 0)
            return;

        var clusterCount = Math.Clamp(1 + forestIconLevel + (presence >= 0.70f ? 1 : 0), 1, 6);
        var canopyBase = groundClass switch
        {
            RegionGroundClass.Sand => new Color(0.36f, 0.46f, 0.20f),
            RegionGroundClass.Snow => new Color(0.28f, 0.40f, 0.28f),
            RegionGroundClass.Mud => new Color(0.18f, 0.38f, 0.20f),
            _ => new Color(0.12f, 0.40f, 0.16f),
        };
        var highlight = canopyBase.Lightened(0.12f);
        var canopyBlend = 0.34f + (presence * 0.34f);

        for (var i = 0; i < clusterCount; i++)
        {
            var centerX = 10 + (int)MathF.Round(SamplePattern01(patternVariant, 2101 + (i * 37)) * (Size - 20));
            var centerY = 10 + (int)MathF.Round(SamplePattern01(patternVariant, 2179 + (i * 41)) * (Size - 20));
            var radius = 2 + (int)MathF.Round((presence * 2.6f) + (SamplePattern01(patternVariant, 2237 + (i * 53)) * 1.8f));
            BlendDisk(image, centerX, centerY, radius, canopyBase, canopyBlend);
            BlendDisk(image, centerX - 1, centerY - 1, Math.Max(1, radius - 1), highlight, canopyBlend * 0.46f);
        }
    }

    private static void DrawRegionReliefPreview(
        Image image,
        RegionGroundClass groundClass,
        float slopeNorm,
        byte mountainIconLevel,
        byte patternVariant)
    {
        var relief = Math.Clamp(Math.Max(slopeNorm, mountainIconLevel / 4f), 0f, 1f);
        if (relief < 0.38f)
            return;

        var ridgeCount = Math.Clamp(1 + mountainIconLevel, 1, 5);
        var shadow = groundClass == RegionGroundClass.Snow
            ? new Color(0.48f, 0.56f, 0.66f)
            : new Color(0.28f, 0.30f, 0.34f);
        var light = groundClass == RegionGroundClass.Snow
            ? new Color(0.94f, 0.96f, 1.00f)
            : new Color(0.84f, 0.84f, 0.86f);

        for (var i = 0; i < ridgeCount; i++)
        {
            var start = new Vector2(
                6 + (SamplePattern01(patternVariant, 2303 + (i * 59)) * 18f),
                16 + (SamplePattern01(patternVariant, 2377 + (i * 61)) * 30f));
            var end = new Vector2(
                32 + (SamplePattern01(patternVariant, 2441 + (i * 67)) * 24f),
                start.Y - (6f + (relief * 8f)) + SampleSignedPattern(patternVariant, 2503 + (i * 71), 4.5f));

            StampSegment(image, start, end, relief >= 0.72f ? 2 : 1, shadow, 0.20f + (relief * 0.24f));
            StampSegment(
                image,
                start + new Vector2(0f, -1f),
                end + new Vector2(1f, -1f),
                1,
                light,
                0.10f + (relief * 0.14f));
        }
    }

    private static void DrawRegionLakePreview(Image image, byte patternVariant)
    {
        var centerX = 35 + (int)MathF.Round(SampleSignedPattern(patternVariant, 2609, 3.5f));
        var centerY = 37 + (int)MathF.Round(SampleSignedPattern(patternVariant, 2647, 4.5f));
        var water = GetWorldGenLakeColor(0.88f);
        BlendDisk(image, centerX, centerY, 7, water, 0.82f);
        BlendDisk(image, centerX - 4, centerY + 2, 4, water.Lightened(0.04f), 0.62f);
        BlendDisk(image, centerX + 3, centerY - 3, 5, water.Darkened(0.05f), 0.58f);
    }

    private static void DrawRegionRiverNetwork(Image image, RegionRiverEdges edges, int width, byte patternVariant)
    {
        var water = GetWorldGenRiverColor(0.82f);
        var anchors = CollectEdgeAnchors(
            RegionRiverEdgeMask.Has(edges, RegionRiverEdges.North),
            RegionRiverEdgeMask.Has(edges, RegionRiverEdges.South),
            RegionRiverEdgeMask.Has(edges, RegionRiverEdges.West),
            RegionRiverEdgeMask.Has(edges, RegionRiverEdges.East),
            patternVariant,
            salt: 2713);

        if (anchors.Count == 0)
        {
            anchors.Add(new Vector2(Size * 0.50f, 0f));
            anchors.Add(new Vector2(Size * 0.52f, Size - 1));
        }

        DrawSeededNetwork(image, anchors, Math.Clamp(width, 2, 8), water, patternVariant, 2801);
    }

    private static void DrawRegionRoadNetwork(Image image, RegionRoadEdges edges, byte patternVariant)
    {
        var road = GetWorldGenRoadColor(0.64f);
        var anchors = CollectEdgeAnchors(
            RegionRoadEdgeMask.Has(edges, RegionRoadEdges.North),
            RegionRoadEdgeMask.Has(edges, RegionRoadEdges.South),
            RegionRoadEdgeMask.Has(edges, RegionRoadEdges.West),
            RegionRoadEdgeMask.Has(edges, RegionRoadEdges.East),
            patternVariant,
            salt: 2903);

        if (anchors.Count == 0)
        {
            anchors.Add(new Vector2(Size * 0.46f, 0f));
            anchors.Add(new Vector2(Size * 0.54f, Size - 1));
        }

        DrawSeededNetwork(image, anchors, 2, road, patternVariant, 3011);
    }

    private static List<Vector2> CollectEdgeAnchors(
        bool north,
        bool south,
        bool west,
        bool east,
        byte patternVariant,
        int salt)
    {
        var anchors = new List<Vector2>(4);
        if (north)
            anchors.Add(new Vector2(
                (Size * 0.50f) + SampleSignedPattern(patternVariant, salt + 11, 8f),
                0f));
        if (south)
            anchors.Add(new Vector2(
                (Size * 0.50f) + SampleSignedPattern(patternVariant, salt + 23, 8f),
                Size - 1));
        if (west)
            anchors.Add(new Vector2(
                0f,
                (Size * 0.50f) + SampleSignedPattern(patternVariant, salt + 37, 8f)));
        if (east)
            anchors.Add(new Vector2(
                Size - 1,
                (Size * 0.50f) + SampleSignedPattern(patternVariant, salt + 53, 8f)));

        return anchors;
    }

    private static void DrawSeededNetwork(
        Image image,
        List<Vector2> anchors,
        int width,
        Color color,
        byte patternVariant,
        int salt)
    {
        var clampedWidth = Math.Clamp(width, 1, 8);
        var center = new Vector2(
            (Size * 0.50f) + SampleSignedPattern(patternVariant, salt + 17, 4.5f),
            (Size * 0.50f) + SampleSignedPattern(patternVariant, salt + 29, 4.5f));

        if (anchors.Count <= 2)
        {
            var start = anchors[0];
            var end = anchors.Count == 2 ? anchors[1] : new Vector2(center.X, Size - 1);
            DrawBentConnection(image, start, end, clampedWidth, color, patternVariant, salt + 61);
            return;
        }

        for (var i = 0; i < anchors.Count; i++)
            DrawBentConnection(image, anchors[i], center, clampedWidth, color, patternVariant, salt + 61 + (i * 37));

        BlendDisk(image, (int)MathF.Round(center.X), (int)MathF.Round(center.Y), Math.Max(1, clampedWidth - 1), color, 0.76f);
    }

    private static void DrawBentConnection(
        Image image,
        Vector2 start,
        Vector2 end,
        int width,
        Color color,
        byte patternVariant,
        int salt)
    {
        var delta = end - start;
        var length = MathF.Max(1f, delta.Length());
        var direction = delta / length;
        var normal = new Vector2(-direction.Y, direction.X);
        var midpoint = start.Lerp(end, 0.5f);
        var bendPoint = midpoint +
                        (normal * SampleSignedPattern(patternVariant, salt + 13, MathF.Min(6.5f, length * 0.12f))) +
                        new Vector2(
                            SampleSignedPattern(patternVariant, salt + 31, 2.5f),
                            SampleSignedPattern(patternVariant, salt + 47, 2.5f));

        StampSegment(image, start, bendPoint, width, color, 0.88f);
        StampSegment(image, bendPoint, end, width, color, 0.88f);
    }

    private static void StampSegment(Image image, Vector2 start, Vector2 end, int width, Color color, float intensity)
    {
        var steps = Math.Max(
            1,
            (int)MathF.Ceiling(MathF.Max(MathF.Abs(end.X - start.X), MathF.Abs(end.Y - start.Y)) * 1.35f));
        var radius = Math.Max(1, width / 2);
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var point = start.Lerp(end, t);
            BlendDisk(image, (int)MathF.Round(point.X), (int)MathF.Round(point.Y), radius, color, intensity);
        }
    }

    private static void BlendDisk(Image image, int centerX, int centerY, int radius, Color color, float intensity)
    {
        var clampedIntensity = Math.Clamp(intensity, 0f, 1f) * Math.Clamp(color.A, 0f, 1f);
        if (clampedIntensity <= 0f || radius <= 0)
            return;

        var radiusSq = radius * radius;
        var opaqueTarget = new Color(color.R, color.G, color.B, 1f);
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if ((dx * dx) + (dy * dy) > radiusSq)
                continue;

            var x = centerX + dx;
            var y = centerY + dy;
            if (x < 0 || y < 0 || x >= Size || y >= Size)
                continue;

            var existing = image.GetPixel(x, y);
            image.SetPixel(x, y, existing.Lerp(opaqueTarget, clampedIntensity));
        }
    }

    private static float SamplePattern01(byte patternVariant, int salt)
    {
        unchecked
        {
            var hash = 2166136261u;
            hash = (hash ^ patternVariant) * 16777619u;
            hash = (hash ^ (uint)salt) * 16777619u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 1023u) / 1023f;
        }
    }

    private static float SampleSignedPattern(byte patternVariant, int salt, float magnitude)
        => ((SamplePattern01(patternVariant, salt) * 2f) - 1f) * magnitude;

    private static void DrawTriangle(Image image, int centerX, int baseY, int height, Color color)
    {
        if (height <= 0)
            return;

        for (var row = 0; row < height; row++)
        {
            var y = baseY - row;
            if (y < 0 || y >= Size)
                continue;

            var halfWidth = Math.Max(0, (int)MathF.Round((row / (float)height) * (height * 0.58f)));
            var startX = Math.Max(0, centerX - halfWidth);
            var endX = Math.Min(Size - 1, centerX + halfWidth);
            for (var x = startX; x <= endX; x++)
                image.SetPixel(x, y, color);
        }
    }

    private static void DrawDisk(Image image, int centerX, int centerY, int radius, Color color)
    {
        var radiusSq = radius * radius;
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            if ((dx * dx) + (dy * dy) > radiusSq)
                continue;

            var x = centerX + dx;
            var y = centerY + dy;
            if (x < 0 || y < 0 || x >= Size || y >= Size)
                continue;

            image.SetPixel(x, y, color);
        }
    }

    private static Texture2D MakeFloor(Color a, Color b)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), a);
        var darkAccent = a.Darkened(0.10f);
        var midAccent = a.Lerp(b, 0.55f);
        var lightAccent = b.Lightened(0.06f);

        for (int y = 2; y < Size - 2; y += 6)
        for (int x = 2; x < Size - 2; x += 6)
        {
            var seed = ((x + 3) * 17 + (y + 5) * 31) % 13;
            if (seed is 0 or 3 or 8)
                FillRect(image, new Rect2I(x, y, 3, 2), midAccent);
            else if (seed is 5 or 10)
                FillRect(image, new Rect2I(x, y, 2, 2), darkAccent);
            else if (seed == 12)
                FillRect(image, new Rect2I(x + 1, y, 2, 1), lightAccent);
        }

        for (int y = 5; y < Size; y += 11)
        {
            var x = 4 + (y * 7 % 18);
            FillRect(image, new Rect2I(x, y, 10, 1), a.Lerp(b, 0.25f));
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeStoneFloor(Color baseColor, Color shadowColor, Color highlightColor)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        for (int y = 4; y < Size - 4; y += 8)
        for (int x = 4; x < Size - 4; x += 8)
        {
            var seed = ((x + 11) * 23 + (y + 7) * 19) % 9;
            if (seed is 0 or 4)
                FillRect(image, new Rect2I(x, y, 3, 3), highlightColor);
            else if (seed is 2 or 7)
                FillRect(image, new Rect2I(x, y + 1, 4, 2), shadowColor);
        }

        for (int y = 10; y < Size; y += 18)
        {
            var x = 6 + (y * 5 % 20);
            FillRect(image, new Rect2I(x, y, 14, 1), shadowColor.Lerp(baseColor, 0.35f));
            FillRect(image, new Rect2I(x + 6, y - 2, 1, 5), shadowColor.Lerp(baseColor, 0.2f));
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeSoilFloor(Color baseColor, Color darkClumpColor, Color lightClumpColor)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        for (int y = 3; y < Size - 3; y += 7)
        for (int x = 3; x < Size - 3; x += 7)
        {
            var seed = ((x + 5) * 13 + (y + 9) * 29) % 11;
            if (seed is 0 or 3 or 6)
                FillRect(image, new Rect2I(x, y, 4, 3), darkClumpColor);
            else if (seed is 8 or 10)
                FillRect(image, new Rect2I(x + 1, y + 1, 2, 2), lightClumpColor);
        }

        for (int y = 8; y < Size; y += 15)
        {
            var x = 5 + (y * 9 % 16);
            FillRect(image, new Rect2I(x, y, 8, 2), baseColor.Lerp(lightClumpColor, 0.35f));
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeGrassFloor(Color baseColor, Color darkBladeColor, Color lightBladeColor, Color dirtPatchColor)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        for (var y = 2; y < Size - 2; y += 4)
        for (var x = 2; x < Size - 2; x += 4)
        {
            var seed = ((x + 17) * 11 + (y + 23) * 29) % 16;
            if (seed is 0 or 4 or 9)
                FillRect(image, new Rect2I(x, y, 2, 2), darkBladeColor);
            else if (seed is 7 or 13)
                FillRect(image, new Rect2I(x + 1, y, 2, 1), lightBladeColor);
            else if (seed == 15)
                FillRect(image, new Rect2I(x, y + 1, 2, 2), dirtPatchColor);
        }

        for (var y = 6; y < Size; y += 12)
        {
            var x = 3 + (y * 5 % 14);
            FillRect(image, new Rect2I(x, y, 6, 2), dirtPatchColor.Lerp(baseColor, 0.35f));
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeSandFloor(Color baseColor, Color shadowColor, Color highlightColor)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        for (var y = 4; y < Size - 4; y += 6)
        for (var x = 4; x < Size - 4; x += 6)
        {
            var seed = ((x + 3) * 17 + (y + 19) * 13) % 12;
            if (seed is 1 or 6 or 9)
                FillRect(image, new Rect2I(x, y, 2, 2), shadowColor);
            else if (seed is 3 or 11)
                FillRect(image, new Rect2I(x + 1, y, 2, 1), highlightColor);
        }

        for (var y = 8; y < Size; y += 14)
        {
            var x = 2 + (y * 7 % 18);
            FillRect(image, new Rect2I(x, y, 12, 2), shadowColor.Lerp(baseColor, 0.35f));
            FillRect(image, new Rect2I(x + 5, y - 1, 8, 1), highlightColor.Lerp(baseColor, 0.22f));
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeMudFloor(Color baseColor, Color deepMudColor, Color wetSheenColor)
    {
        var image = NewImage();
        DrawMudPatternFill(image, baseColor, deepMudColor, wetSheenColor);

        return ImageTexture.CreateFromImage(image);
    }

    private static void DrawMudPatternFill(Image image, Color baseColor, Color deepMudColor, Color wetSheenColor)
    {
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        for (var y = 3; y < Size - 3; y += 7)
        for (var x = 3; x < Size - 3; x += 7)
        {
            var seed = ((x + 13) * 19 + (y + 5) * 11) % 14;
            if (seed is 0 or 4 or 8)
                FillRect(image, new Rect2I(x, y, 4, 3), deepMudColor);
            else if (seed is 10 or 12)
                FillRect(image, new Rect2I(x + 1, y + 1, 3, 2), new Color(wetSheenColor.R, wetSheenColor.G, wetSheenColor.B, 0.78f));
        }

        for (var y = 10; y < Size; y += 16)
        {
            var x = 6 + (y * 5 % 12);
            FillRect(image, new Rect2I(x, y, 10, 2), deepMudColor.Lerp(baseColor, 0.30f));
            FillRect(image, new Rect2I(x + 2, y + 1, 6, 1), new Color(wetSheenColor.R, wetSheenColor.G, wetSheenColor.B, 0.62f));
        }
    }

    private static void DrawMudPatternBlend(Image image, Color baseColor, Color deepMudColor, Color wetSheenColor, float intensity)
    {
        if (intensity <= 0f)
            return;

        BlendRect(image, new Rect2I(0, 0, Size, Size), baseColor, 0.20f * intensity);

        for (var y = 3; y < Size - 3; y += 7)
        for (var x = 3; x < Size - 3; x += 7)
        {
            var seed = ((x + 13) * 19 + (y + 5) * 11) % 14;
            if (seed is 0 or 4 or 8)
                BlendRect(image, new Rect2I(x, y, 4, 3), deepMudColor, 0.22f * intensity);
            else if (seed is 10 or 12)
                BlendRect(image, new Rect2I(x + 1, y + 1, 3, 2), wetSheenColor, 0.14f * intensity);
        }

        for (var y = 10; y < Size; y += 16)
        {
            var x = 6 + (y * 5 % 12);
            BlendRect(image, new Rect2I(x, y, 10, 2), deepMudColor.Lerp(baseColor, 0.30f), 0.20f * intensity);
            BlendRect(image, new Rect2I(x + 2, y + 1, 6, 1), wetSheenColor, 0.12f * intensity);
        }
    }

    private static Texture2D MakeSnowFloor(Color baseColor, Color shadowColor, Color sparkleColor)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        for (var y = 2; y < Size - 2; y += 5)
        for (var x = 2; x < Size - 2; x += 5)
        {
            var seed = ((x + 7) * 23 + (y + 29) * 17) % 20;
            if (seed is 0 or 5 or 12)
                FillRect(image, new Rect2I(x, y, 2, 2), shadowColor);
            else if (seed is 3 or 9 or 16)
                FillRect(image, new Rect2I(x + 1, y + 1, 1, 1), sparkleColor);
        }

        for (var y = 9; y < Size; y += 15)
        {
            var x = 4 + (y * 9 % 16);
            FillRect(image, new Rect2I(x, y, 12, 1), shadowColor.Lerp(baseColor, 0.45f));
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeBrickFloor(Color a, Color mortar)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), mortar);
        for (int y = 2; y < Size; y += 14)
            for (int x = ((y / 14) % 2 == 0 ? 2 : 10); x < Size; x += 16)
                FillRect(image, new Rect2I(x, y, 12, 10), a);
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeWater()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(3, 3, Size - 6, Size - 6), new Color(0.10f, 0.30f, 0.65f, 0.16f));
        FillRect(image, new Rect2I(6, 6, Size - 12, Size - 12), new Color(0.18f, 0.46f, 0.82f, 0.12f));
        for (int y = 10; y < Size - 8; y += 12)
            FillRect(image, new Rect2I(8, y, 48, 2), new Color(0.70f, 0.90f, 0.98f, 0.55f));
        for (int y = 16; y < Size - 6; y += 14)
            FillRect(image, new Rect2I(14, y, 36, 1), new Color(0.84f, 0.97f, 1f, 0.45f));
        Outline(image, new Color(0.18f, 0.44f, 0.76f, 0.45f), new Rect2I(3, 3, Size - 6, Size - 6));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeMagma()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0.54f, 0.14f, 0.06f));
        for (int y = 6; y < Size; y += 10)
            FillRect(image, new Rect2I(4, y, 56, 3), new Color(0.95f, 0.48f, 0.10f, 0.85f));
        for (int y = 10; y < Size; y += 14)
            FillRect(image, new Rect2I(10, y, 44, 2), new Color(1.00f, 0.76f, 0.20f, 0.75f));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeRockWall(Color baseColor, Color crackColor, Color highlight)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), baseColor);

        // Chunky wall blocks so underground walls are visually distinct from floors.
        for (int y = 4; y < Size; y += 12)
        for (int x = 4; x < Size; x += 12)
        {
            FillRect(image, new Rect2I(x, y, 8, 8), baseColor.Darkened(0.05f));
            if (((x + y) / 12) % 3 == 0)
                FillRect(image, new Rect2I(x + 2, y + 2, 4, 2), highlight);
        }

        for (int i = 0; i < Size; i += 8)
        {
            FillRect(image, new Rect2I(i, i / 2, 2, 6), crackColor);
            var mirrorX = Size - 2 - i;
            FillRect(image, new Rect2I(mirrorX, i / 2 + 10, 2, 4), crackColor.Darkened(0.1f));
        }

        Outline(image, baseColor.Darkened(0.30f));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeRampTile()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0.44f, 0.44f, 0.49f));
        for (int step = 0; step < 7; step++)
        {
            var x = 6 + step * 8;
            var y = 40 - step * 4;
            FillRect(image, new Rect2I(x, y, 14, 6), new Color(0.64f, 0.64f, 0.69f));
        }

        Outline(image, new Color(0.24f, 0.24f, 0.28f));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeTree()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(26, 34, 12, 22), new Color(0.42f, 0.25f, 0.11f));
        FillRect(image, new Rect2I(12, 8, 40, 28), new Color(0.18f, 0.53f, 0.20f));
        FillRect(image, new Rect2I(18, 4, 28, 12), new Color(0.24f, 0.66f, 0.24f));
        FillRect(image, new Rect2I(16, 14, 4, 4), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(42, 18, 5, 4), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(28, 10, 6, 3), new Color(0.33f, 0.75f, 0.31f, 0.85f));
        return CreateOutlinedTexture(image, new Color(0.07f, 0.20f, 0.08f, 0.95f));
    }

    private static Texture2D MakeStair()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0.42f, 0.42f, 0.46f));
        for (int i = 0; i < 5; i++)
            FillRect(image, new Rect2I(10 + i * 8, 14 + i * 7, 36 - i * 4, 5), new Color(0.73f, 0.73f, 0.77f));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D MakeDwarf(DwarfAppearanceComponent appearance)
    {
        var image = NewImage();
        var faceRect = ResolveFaceRect(appearance.FaceType);
        var tunic = ResolveDwarfTunicColor(appearance);
        var skin = ResolveDwarfSkinColor(appearance);
        var hair = ResolveHairColor(appearance.HairColor);
        var beard = ResolveHairColor(appearance.BeardColor);
        var boot = new Color(0.28f, 0.19f, 0.11f);

        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        DrawDwarfFace(image, faceRect, appearance.FaceType, skin);
        FillRect(image, new Rect2I(15, 24, 34, 24), tunic);
        FillRect(image, new Rect2I(13, 28, 8, 14), tunic.Darkened(0.08f));
        FillRect(image, new Rect2I(43, 28, 8, 14), tunic.Darkened(0.08f));
        FillRect(image, new Rect2I(18, 30, 28, 18), tunic.Lightened(0.05f));
        FillRect(image, new Rect2I(16, 48, 12, 12), tunic.Darkened(0.18f));
        FillRect(image, new Rect2I(36, 48, 12, 12), tunic.Darkened(0.18f));
        FillRect(image, new Rect2I(16, 58, 12, 4), boot);
        FillRect(image, new Rect2I(36, 58, 12, 4), boot);
        FillRect(image, new Rect2I(20, 26, 24, 3), new Color(0.88f, 0.82f, 0.62f, 0.38f));

        DrawDwarfHair(image, faceRect, appearance.HairType, hair);
        DrawDwarfEyes(image, faceRect, appearance.EyeType);
        DrawDwarfNose(image, faceRect, appearance.NoseType, skin.Darkened(0.22f));
        DrawDwarfMouth(image, faceRect, appearance.MouthType);
        DrawDwarfBeard(image, faceRect, appearance.BeardType, beard);

        return CreateOutlinedTexture(image, tunic.Darkened(0.50f));
    }

    private static Rect2I ResolveFaceRect(DwarfFaceType faceType)
        => faceType switch
        {
            DwarfFaceType.Round => new Rect2I(18, 8, 28, 19),
            DwarfFaceType.Square => new Rect2I(18, 8, 28, 18),
            DwarfFaceType.Long => new Rect2I(20, 6, 24, 23),
            DwarfFaceType.Wide => new Rect2I(16, 9, 32, 17),
            _ => new Rect2I(18, 8, 28, 19),
        };

    private static void DrawDwarfFace(Image image, Rect2I faceRect, DwarfFaceType faceType, Color skin)
    {
        var rowInsets = faceType switch
        {
            DwarfFaceType.Round => new[] { 6, 4, 3, 2, 1, 1, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 4, 6, 8 },
            DwarfFaceType.Square => new[] { 3, 2, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 2, 3 },
            DwarfFaceType.Long => new[] { 5, 4, 3, 3, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 3, 3, 4, 5, 6, 8 },
            DwarfFaceType.Wide => new[] { 5, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 5, 7 },
            _ => new[] { 6, 4, 3, 2, 1, 1, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 4, 6, 8 },
        };

        for (int row = 0; row < rowInsets.Length; row++)
        {
            var y = faceRect.Position.Y + row;
            var inset = rowInsets[row];
            var width = faceRect.Size.X - (inset * 2);
            if (width <= 0)
                continue;

            FillRect(image, new Rect2I(faceRect.Position.X + inset, y, width, 1), skin);
        }

        var cheek = skin.Lightened(0.06f);
        var jaw = skin.Darkened(0.09f);
        var forehead = skin.Lightened(0.04f);
        FillRect(image, new Rect2I(faceRect.Position.X + 5, faceRect.Position.Y + 2, Math.Max(8, faceRect.Size.X - 10), 2), forehead);
        FillRect(image, new Rect2I(faceRect.Position.X + 3, faceRect.Position.Y + 7, 3, Math.Max(4, faceRect.Size.Y / 3)), cheek);
        FillRect(image, new Rect2I(faceRect.End.X - 6, faceRect.Position.Y + 7, 3, Math.Max(4, faceRect.Size.Y / 3)), cheek);
        FillRect(image, new Rect2I(faceRect.Position.X + 4, faceRect.End.Y - 4, Math.Max(10, faceRect.Size.X - 8), 2), jaw);
    }

    private static Color ResolveDwarfTunicColor(DwarfAppearanceComponent appearance)
    {
        var palette = new[]
        {
            new Color(0.78f, 0.34f, 0.18f),
            new Color(0.26f, 0.48f, 0.76f),
            new Color(0.22f, 0.60f, 0.34f),
            new Color(0.68f, 0.28f, 0.54f),
            new Color(0.64f, 0.52f, 0.20f),
            new Color(0.42f, 0.44f, 0.72f),
        };
        var index = Math.Abs(appearance.Signature.GetHashCode()) % palette.Length;
        return palette[index];
    }

    private static Color ResolveDwarfSkinColor(DwarfAppearanceComponent appearance)
    {
        var palette = new[]
        {
            new Color(0.96f, 0.84f, 0.67f),
            new Color(0.90f, 0.76f, 0.58f),
            new Color(0.84f, 0.66f, 0.50f),
            new Color(0.72f, 0.54f, 0.38f),
        };
        var index = Math.Abs((appearance.Signature.GetHashCode() >> 3)) % palette.Length;
        return palette[index];
    }

    private static Color ResolveHairColor(DwarfHairColor color)
        => color switch
        {
            DwarfHairColor.Coal => new Color(0.12f, 0.10f, 0.10f),
            DwarfHairColor.Chestnut => new Color(0.40f, 0.20f, 0.10f),
            DwarfHairColor.Copper => new Color(0.69f, 0.34f, 0.14f),
            DwarfHairColor.Blond => new Color(0.82f, 0.72f, 0.40f),
            DwarfHairColor.Ash => new Color(0.42f, 0.40f, 0.38f),
            DwarfHairColor.Silver => new Color(0.72f, 0.72f, 0.76f),
            _ => new Color(0.12f, 0.10f, 0.10f),
        };

    private static void DrawDwarfHair(Image image, Rect2I faceRect, DwarfHairType hairType, Color hairColor)
    {
        var centerX = faceRect.Position.X + (faceRect.Size.X / 2);
        switch (hairType)
        {
            case DwarfHairType.Bald:
                return;

            case DwarfHairType.Crop:
                DrawCenteredRows(image, centerX, faceRect.Position.Y - 2, new[] { 14, 18, 20, 18, 14 }, hairColor);
                DrawCenteredRows(image, centerX, faceRect.Position.Y - 1, new[] { 10, 14 }, hairColor.Lightened(0.07f));
                break;

            case DwarfHairType.Swept:
                DrawOffsetRows(image, centerX - 1, faceRect.Position.Y - 3, new[] { 12, 17, 22, 24, 20 }, new[] { -2, -1, 0, 1, 2 }, hairColor);
                DrawOffsetRows(image, faceRect.End.X - 4, faceRect.Position.Y + 1, new[] { 4, 4, 3, 2 }, new[] { 0, 1, 1, 2 }, hairColor.Lightened(0.08f));
                break;

            case DwarfHairType.Shaggy:
                DrawCenteredRows(image, centerX, faceRect.Position.Y - 2, new[] { 14, 20, 24, 24, 22, 18 }, hairColor);
                DrawOffsetRows(image, faceRect.Position.X + 1, faceRect.Position.Y + 3, new[] { 4, 5, 4, 3 }, new[] { 0, -1, 0, -1 }, hairColor);
                DrawOffsetRows(image, faceRect.End.X - 4, faceRect.Position.Y + 4, new[] { 4, 5, 4, 3 }, new[] { 0, 1, 0, 1 }, hairColor);
                break;

            case DwarfHairType.Crest:
                DrawCenteredRows(image, centerX, faceRect.Position.Y - 5, new[] { 2, 4, 6, 6, 5, 4, 3, 2 }, hairColor);
                break;

            case DwarfHairType.Braided:
                DrawCenteredRows(image, centerX, faceRect.Position.Y - 2, new[] { 12, 18, 20, 18, 14 }, hairColor);
                DrawVerticalTaper(image, faceRect.Position.X + 3, faceRect.End.Y - 3, new[] { 3, 3, 2, 2, 2, 1, 1 }, hairColor);
                DrawVerticalTaper(image, faceRect.End.X - 6, faceRect.End.Y - 3, new[] { 3, 3, 2, 2, 2, 1, 1 }, hairColor);
                break;
        }
    }

    private static void DrawDwarfEyes(Image image, Rect2I faceRect, DwarfEyeType eyeType)
    {
        var eyeColor = new Color(0.08f, 0.07f, 0.07f);
        var leftX = faceRect.Position.X + 6;
        var rightX = faceRect.End.X - 8;
        var y = faceRect.Position.Y + 7;

        switch (eyeType)
        {
            case DwarfEyeType.Dot:
                FillRect(image, new Rect2I(leftX, y, 2, 2), eyeColor);
                FillRect(image, new Rect2I(rightX, y, 2, 2), eyeColor);
                break;

            case DwarfEyeType.Narrow:
                FillRect(image, new Rect2I(leftX - 1, y, 4, 1), eyeColor);
                FillRect(image, new Rect2I(rightX - 1, y, 4, 1), eyeColor);
                break;

            case DwarfEyeType.Wide:
                FillRect(image, new Rect2I(leftX - 1, y, 4, 2), eyeColor);
                FillRect(image, new Rect2I(rightX - 1, y, 4, 2), eyeColor);
                break;

            case DwarfEyeType.HeavyBrow:
                FillRect(image, new Rect2I(leftX - 1, y - 2, 4, 1), eyeColor);
                FillRect(image, new Rect2I(rightX - 1, y - 2, 4, 1), eyeColor);
                FillRect(image, new Rect2I(leftX, y, 2, 2), eyeColor);
                FillRect(image, new Rect2I(rightX, y, 2, 2), eyeColor);
                break;

            case DwarfEyeType.Bright:
                FillRect(image, new Rect2I(leftX - 1, y, 4, 2), new Color(0.98f, 0.98f, 1f));
                FillRect(image, new Rect2I(rightX - 1, y, 4, 2), new Color(0.98f, 0.98f, 1f));
                FillRect(image, new Rect2I(leftX, y, 2, 2), eyeColor);
                FillRect(image, new Rect2I(rightX, y, 2, 2), eyeColor);
                break;
        }
    }

    private static void DrawDwarfNose(Image image, Rect2I faceRect, DwarfNoseType noseType, Color noseColor)
    {
        var centerX = faceRect.Position.X + (faceRect.Size.X / 2);
        var y = faceRect.Position.Y + 9;
        switch (noseType)
        {
            case DwarfNoseType.Button:
                FillRect(image, new Rect2I(centerX - 1, y + 1, 2, 2), noseColor);
                break;

            case DwarfNoseType.Broad:
                FillRect(image, new Rect2I(centerX - 2, y + 1, 4, 2), noseColor);
                break;

            case DwarfNoseType.Long:
                FillRect(image, new Rect2I(centerX - 1, y, 2, 5), noseColor);
                break;

            case DwarfNoseType.Hooked:
                FillRect(image, new Rect2I(centerX - 1, y, 2, 4), noseColor);
                FillRect(image, new Rect2I(centerX - 1, y + 3, 3, 2), noseColor);
                break;
        }
    }

    private static void DrawDwarfMouth(Image image, Rect2I faceRect, DwarfMouthType mouthType)
    {
        var lip = new Color(0.32f, 0.12f, 0.12f);
        var x = faceRect.Position.X + (faceRect.Size.X / 2) - 4;
        var y = faceRect.End.Y - 5;
        switch (mouthType)
        {
            case DwarfMouthType.Neutral:
                FillRect(image, new Rect2I(x, y, 8, 1), lip);
                break;

            case DwarfMouthType.Smile:
                FillRect(image, new Rect2I(x + 1, y, 6, 1), lip);
                FillRect(image, new Rect2I(x, y - 1, 1, 1), lip);
                FillRect(image, new Rect2I(x + 7, y - 1, 1, 1), lip);
                break;

            case DwarfMouthType.Smirk:
                FillRect(image, new Rect2I(x, y, 8, 1), lip);
                FillRect(image, new Rect2I(x + 6, y - 1, 2, 1), lip);
                break;

            case DwarfMouthType.Frown:
                FillRect(image, new Rect2I(x + 1, y, 6, 1), lip);
                FillRect(image, new Rect2I(x, y + 1, 1, 1), lip);
                FillRect(image, new Rect2I(x + 7, y + 1, 1, 1), lip);
                break;

            case DwarfMouthType.Open:
                FillRect(image, new Rect2I(x + 2, y, 4, 2), lip);
                break;
        }
    }

    private static void DrawDwarfBeard(Image image, Rect2I faceRect, DwarfBeardType beardType, Color beardColor)
    {
        var centerX = faceRect.Position.X + (faceRect.Size.X / 2);
        var beardTop = faceRect.Position.Y + faceRect.Size.Y - 5;
        switch (beardType)
        {
            case DwarfBeardType.Clean:
                return;

            case DwarfBeardType.Short:
                DrawCenteredRows(image, centerX, beardTop, new[] { 18, 20, 18, 14 }, beardColor);
                break;

            case DwarfBeardType.Full:
                DrawCenteredRows(image, centerX, beardTop - 1, new[] { 18, 22, 24, 24, 22, 20, 18, 16, 14 }, beardColor);
                break;

            case DwarfBeardType.Braided:
                DrawCenteredRows(image, centerX, beardTop - 1, new[] { 18, 22, 22, 20, 18 }, beardColor);
                DrawVerticalTaper(image, centerX - 3, beardTop + 4, new[] { 6, 6, 5, 4, 4, 3, 2, 2 }, beardColor);
                break;

            case DwarfBeardType.Forked:
                DrawCenteredRows(image, centerX, beardTop - 1, new[] { 18, 22, 22, 20, 18 }, beardColor);
                DrawVerticalTaper(image, faceRect.Position.X + 4, beardTop + 4, new[] { 5, 5, 4, 4, 3, 3, 2 }, beardColor);
                DrawVerticalTaper(image, faceRect.End.X - 9, beardTop + 4, new[] { 5, 5, 4, 4, 3, 3, 2 }, beardColor);
                break;

            case DwarfBeardType.Mutton:
                DrawOffsetRows(image, faceRect.Position.X + 2, beardTop - 2, new[] { 4, 5, 5, 4, 4, 3 }, new[] { 0, -1, -1, 0, 0, 1 }, beardColor);
                DrawOffsetRows(image, faceRect.End.X - 6, beardTop - 2, new[] { 4, 5, 5, 4, 4, 3 }, new[] { 0, 1, 1, 0, 0, -1 }, beardColor);
                DrawCenteredRows(image, centerX, beardTop + 1, new[] { 12, 14, 12 }, beardColor);
                break;
        }
    }

    private static void DrawCenteredRows(Image image, int centerX, int topY, int[] widths, Color color)
    {
        for (int row = 0; row < widths.Length; row++)
        {
            var width = widths[row];
            if (width <= 0)
                continue;

            var x = centerX - (width / 2);
            FillRect(image, new Rect2I(x, topY + row, width, 1), color);
        }
    }

    private static void DrawOffsetRows(Image image, int startX, int topY, int[] widths, int[] xOffsets, Color color)
    {
        var rowCount = Math.Min(widths.Length, xOffsets.Length);
        for (int row = 0; row < rowCount; row++)
        {
            var width = widths[row];
            if (width <= 0)
                continue;

            FillRect(image, new Rect2I(startX + xOffsets[row], topY + row, width, 1), color);
        }
    }

    private static void DrawVerticalTaper(Image image, int x, int topY, int[] widths, Color color)
    {
        for (int row = 0; row < widths.Length; row++)
        {
            var width = widths[row];
            if (width <= 0)
                continue;

            FillRect(image, new Rect2I(x, topY + row, width, 1), color);
        }
    }

    private static Texture2D MakeGoblin()
    {
        var image = NewImage();
        var skin = new Color(0.44f, 0.78f, 0.33f);
        var cloth = new Color(0.26f, 0.34f, 0.18f);

        FillRect(image, new Rect2I(23, 10, 18, 12), skin);
        FillRect(image, new Rect2I(19, 13, 4, 5), skin.Darkened(0.05f));
        FillRect(image, new Rect2I(41, 13, 4, 5), skin.Darkened(0.05f));
        FillRect(image, new Rect2I(19, 24, 26, 20), cloth);
        FillRect(image, new Rect2I(15, 26, 4, 14), cloth.Darkened(0.10f));
        FillRect(image, new Rect2I(45, 26, 4, 14), cloth.Darkened(0.10f));
        FillRect(image, new Rect2I(21, 44, 8, 14), cloth.Darkened(0.16f));
        FillRect(image, new Rect2I(35, 44, 8, 14), cloth.Darkened(0.16f));
        FillRect(image, new Rect2I(28, 15, 2, 2), new Color(0.80f, 0.12f, 0.10f));
        FillRect(image, new Rect2I(34, 15, 2, 2), new Color(0.80f, 0.12f, 0.10f));
        return CreateOutlinedTexture(image, new Color(0.14f, 0.24f, 0.12f, 0.95f));
    }

    private static Texture2D MakeTroll()
    {
        var image = NewImage();
        var hide = new Color(0.58f, 0.66f, 0.56f);
        var shadow = hide.Darkened(0.22f);

        FillRect(image, new Rect2I(19, 8, 26, 14), hide);
        FillRect(image, new Rect2I(14, 24, 36, 22), hide);
        FillRect(image, new Rect2I(10, 26, 6, 18), shadow);
        FillRect(image, new Rect2I(48, 26, 6, 18), shadow);
        FillRect(image, new Rect2I(18, 46, 10, 14), shadow);
        FillRect(image, new Rect2I(36, 46, 10, 14), shadow);
        FillRect(image, new Rect2I(23, 14, 4, 2), new Color(0.16f, 0.18f, 0.16f));
        FillRect(image, new Rect2I(37, 14, 4, 2), new Color(0.16f, 0.18f, 0.16f));
        FillRect(image, new Rect2I(28, 20, 3, 3), new Color(0.90f, 0.88f, 0.80f));
        FillRect(image, new Rect2I(33, 20, 3, 3), new Color(0.90f, 0.88f, 0.80f));
        return CreateOutlinedTexture(image, new Color(0.20f, 0.28f, 0.20f, 0.95f));
    }

    private static Texture2D MakeCat()
    {
        var image = NewImage();
        var fur = new Color(0.79f, 0.61f, 0.25f);
        var furDark = fur.Darkened(0.22f);

        FillRect(image, new Rect2I(16, 30, 26, 12), fur);
        FillRect(image, new Rect2I(40, 24, 12, 10), fur.Lightened(0.04f));
        FillRect(image, new Rect2I(42, 20, 3, 4), fur);
        FillRect(image, new Rect2I(48, 20, 3, 4), fur);
        FillRect(image, new Rect2I(12, 26, 4, 10), furDark);
        FillRect(image, new Rect2I(20, 42, 4, 10), furDark);
        FillRect(image, new Rect2I(34, 42, 4, 10), furDark);
        FillRect(image, new Rect2I(22, 32, 3, 10), furDark);
        FillRect(image, new Rect2I(30, 32, 3, 10), furDark);
        return CreateOutlinedTexture(image, new Color(0.30f, 0.20f, 0.10f, 0.95f));
    }

    private static Texture2D MakeDog()
    {
        var image = NewImage();
        var fur = new Color(0.48f, 0.34f, 0.20f);
        var furLight = fur.Lightened(0.10f);
        var furDark = fur.Darkened(0.20f);

        FillRect(image, new Rect2I(12, 28, 34, 14), fur);
        FillRect(image, new Rect2I(44, 22, 14, 12), furLight);
        FillRect(image, new Rect2I(54, 26, 4, 4), furDark);
        FillRect(image, new Rect2I(46, 20, 4, 6), furDark);
        FillRect(image, new Rect2I(8, 24, 4, 10), furDark);
        FillRect(image, new Rect2I(16, 42, 5, 12), furDark);
        FillRect(image, new Rect2I(32, 42, 5, 12), furDark);
        FillRect(image, new Rect2I(22, 32, 8, 6), new Color(0.82f, 0.74f, 0.60f));
        return CreateOutlinedTexture(image, new Color(0.23f, 0.15f, 0.09f, 0.95f));
    }

    private static Texture2D MakeElk()
    {
        var image = NewImage();
        var coat = new Color(0.57f, 0.37f, 0.20f);
        var antler = new Color(0.84f, 0.75f, 0.60f);
        var legs = coat.Darkened(0.16f);

        FillRect(image, new Rect2I(10, 26, 38, 14), coat);
        FillRect(image, new Rect2I(40, 18, 8, 12), coat);
        FillRect(image, new Rect2I(46, 16, 10, 8), coat.Darkened(0.06f));
        FillRect(image, new Rect2I(14, 40, 4, 18), legs);
        FillRect(image, new Rect2I(24, 40, 4, 18), legs);
        FillRect(image, new Rect2I(34, 40, 4, 18), legs);
        FillRect(image, new Rect2I(44, 40, 4, 18), legs);
        FillRect(image, new Rect2I(48, 10, 2, 8), antler);
        FillRect(image, new Rect2I(51, 10, 2, 8), antler);
        FillRect(image, new Rect2I(46, 8, 10, 2), antler);
        FillRect(image, new Rect2I(45, 6, 4, 2), antler);
        FillRect(image, new Rect2I(53, 6, 4, 2), antler);
        FillRect(image, new Rect2I(12, 28, 6, 8), new Color(0.84f, 0.72f, 0.58f));
        return CreateOutlinedTexture(image, new Color(0.28f, 0.17f, 0.08f, 0.95f));
    }

    private static Texture2D MakeGiantCarp()
    {
        var image = NewImage();
        var body = new Color(0.26f, 0.58f, 0.78f);
        var fin = new Color(0.18f, 0.46f, 0.68f);
        var scale = new Color(0.52f, 0.78f, 0.90f, 0.85f);

        FillRect(image, new Rect2I(14, 24, 34, 16), body);
        FillRect(image, new Rect2I(10, 26, 6, 12), body.Lightened(0.08f));
        FillRect(image, new Rect2I(48, 26, 8, 12), fin);
        FillRect(image, new Rect2I(54, 22, 4, 6), fin);
        FillRect(image, new Rect2I(54, 36, 4, 6), fin);
        FillRect(image, new Rect2I(28, 18, 8, 6), fin.Lightened(0.06f));
        FillRect(image, new Rect2I(26, 40, 8, 4), fin);
        FillRect(image, new Rect2I(22, 28, 3, 8), scale);
        FillRect(image, new Rect2I(30, 28, 3, 8), scale);
        FillRect(image, new Rect2I(38, 28, 3, 8), scale);
        FillRect(image, new Rect2I(13, 30, 2, 2), new Color(0.96f, 0.96f, 0.98f));
        FillRect(image, new Rect2I(14, 30, 1, 1), new Color(0.05f, 0.05f, 0.08f));
        return CreateOutlinedTexture(image, new Color(0.12f, 0.28f, 0.42f, 0.95f));
    }

    private static Texture2D MakeCreatureFromId(string id)
    {
        var hash = StableHash(id);
        var red = HashChannel(hash, 0, 0.30f, 0.84f);
        var green = HashChannel(hash, 8, 0.26f, 0.82f);
        var blue = HashChannel(hash, 16, 0.20f, 0.78f);
        var body = new Color(red, green, blue);

        if ((hash & 1) == 0)
            return MakeAnimal(body);

        var face = new Color(
            HashChannel(hash, 5, 0.62f, 0.96f),
            HashChannel(hash, 11, 0.54f, 0.88f),
            HashChannel(hash, 17, 0.46f, 0.80f));
        return MakeActor(body, face);
    }

    private static Texture2D MakeActor(Color body, Color face)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(24, 10, 16, 14), face);
        FillRect(image, new Rect2I(18, 24, 28, 24), body);
        FillRect(image, new Rect2I(18, 48, 8, 12), body.Darkened(0.15f));
        FillRect(image, new Rect2I(38, 48, 8, 12), body.Darkened(0.15f));
        return CreateOutlinedTexture(image, body.Darkened(0.45f));
    }

    private static Texture2D MakeAnimal(Color body)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(14, 24, 34, 16), body);
        FillRect(image, new Rect2I(40, 18, 14, 12), body.Lightened(0.08f));
        FillRect(image, new Rect2I(18, 40, 6, 12), body.Darkened(0.1f));
        FillRect(image, new Rect2I(38, 40, 6, 12), body.Darkened(0.1f));
        return CreateOutlinedTexture(image, body.Darkened(0.45f));
    }

    private static float HashChannel(int hash, int shift, float min, float max)
    {
        var value = (hash >> shift) & 0xFF;
        var t = value / 255f;
        return min + ((max - min) * t);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static bool TryMakeDerivedResourceItem(string itemDefId, string? materialId, out Texture2D texture)
    {
        if (IsDerivedResourceForm(itemDefId, ContentFormRoles.Log))
        {
            var palette = ResolveWoodItemPalette(ResolveDerivedResourceMaterialId(itemDefId, materialId, ContentFormRoles.Log));
            texture = MakeLog(palette.Base, palette.Highlight, palette.Shadow);
            return true;
        }

        if (IsDerivedResourceForm(itemDefId, ContentFormRoles.Plank))
        {
            var palette = ResolveWoodItemPalette(ResolveDerivedResourceMaterialId(itemDefId, materialId, ContentFormRoles.Plank));
            texture = MakePlank(palette.Base, palette.Highlight, palette.Shadow);
            return true;
        }

        if (IsDerivedResourceForm(itemDefId, ContentFormRoles.Boulder))
        {
            var palette = ResolveStoneItemPalette(ResolveDerivedResourceMaterialId(itemDefId, materialId, ContentFormRoles.Boulder));
            texture = MakeBoulder(palette.Base, palette.Highlight, palette.Shadow);
            return true;
        }

        if (IsDerivedResourceForm(itemDefId, ContentFormRoles.Ore))
        {
            var (rock, ore) = ResolveOreChunkPalette(ResolveDerivedResourceMaterialId(itemDefId, materialId, ContentFormRoles.Ore));
            texture = MakeOreChunk(rock, ore);
            return true;
        }

        if (IsDerivedResourceForm(itemDefId, ContentFormRoles.Bar))
        {
            var palette = ResolveMetalItemPalette(ResolveDerivedResourceMaterialId(itemDefId, materialId, ContentFormRoles.Bar));
            texture = MakeIngot(palette.Highlight, palette.Shadow);
            return true;
        }

        if (string.Equals(itemDefId, ItemDefIds.Seed, StringComparison.OrdinalIgnoreCase) ||
            itemDefId.EndsWith("_seed", StringComparison.OrdinalIgnoreCase) ||
            ClientContentQueries.HasItemTag(itemDefId, TagIds.Seed))
        {
            var palette = ResolveSeedItemPalette(ResolveDerivedResourceMaterialId(itemDefId, materialId, "seed"));
            texture = MakeSeeds(palette.Base, palette.Highlight, palette.Shadow);
            return true;
        }

        texture = null!;
        return false;
    }

    private static bool IsDerivedResourceForm(string itemDefId, string role)
        => string.Equals(itemDefId, role, StringComparison.OrdinalIgnoreCase) ||
           itemDefId.EndsWith("_" + role, StringComparison.OrdinalIgnoreCase) ||
           !string.IsNullOrWhiteSpace(ClientContentQueries.ResolveMaterialIdForFormItemDefId(itemDefId, role));

    private static string? ResolveDerivedResourceMaterialId(string itemDefId, string? materialId, string role)
    {
        if (!string.IsNullOrWhiteSpace(materialId))
            return materialId;

        if (string.Equals(itemDefId, role, StringComparison.OrdinalIgnoreCase))
        {
            return role switch
            {
                ContentFormRoles.Log or ContentFormRoles.Plank => MaterialIds.Wood,
                ContentFormRoles.Boulder => MaterialIds.Granite,
                _ => null,
            };
        }

        var resolvedFromContent = ClientContentQueries.ResolveMaterialIdForFormItemDefId(itemDefId, role);
        if (!string.IsNullOrWhiteSpace(resolvedFromContent))
            return resolvedFromContent;

        var suffix = "_" + role;
        return itemDefId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? itemDefId[..^suffix.Length]
            : null;
    }

    private static TilePalette ResolveWoodItemPalette(string? materialId) => materialId switch
    {
        "oak" or "oak_wood" => new TilePalette(new Color(0.57f, 0.37f, 0.18f), new Color(0.28f, 0.17f, 0.08f), new Color(0.82f, 0.68f, 0.44f)),
        "birch" or "birch_wood" => new TilePalette(new Color(0.76f, 0.66f, 0.42f), new Color(0.32f, 0.24f, 0.12f), new Color(0.90f, 0.82f, 0.62f)),
        "pine" or "pine_wood" => new TilePalette(new Color(0.60f, 0.44f, 0.20f), new Color(0.26f, 0.16f, 0.08f), new Color(0.84f, 0.72f, 0.40f)),
        "spruce" or "spruce_wood" => new TilePalette(new Color(0.48f, 0.34f, 0.16f), new Color(0.22f, 0.14f, 0.07f), new Color(0.72f, 0.58f, 0.32f)),
        "willow" or "willow_wood" => new TilePalette(new Color(0.62f, 0.50f, 0.28f), new Color(0.28f, 0.20f, 0.10f), new Color(0.86f, 0.76f, 0.46f)),
        "palm" or "palm_wood" => new TilePalette(new Color(0.73f, 0.58f, 0.28f), new Color(0.32f, 0.22f, 0.10f), new Color(0.90f, 0.80f, 0.52f)),
        "baobab" or "baobab_wood" => new TilePalette(new Color(0.64f, 0.42f, 0.20f), new Color(0.28f, 0.18f, 0.09f), new Color(0.88f, 0.74f, 0.42f)),
        "apple" or "apple_wood" => new TilePalette(new Color(0.66f, 0.46f, 0.22f), new Color(0.30f, 0.18f, 0.09f), new Color(0.88f, 0.76f, 0.46f)),
        "fig" or "fig_wood" => new TilePalette(new Color(0.54f, 0.34f, 0.20f), new Color(0.24f, 0.13f, 0.08f), new Color(0.78f, 0.60f, 0.38f)),
        "deadwood" or "deadwood_wood" => new TilePalette(new Color(0.58f, 0.52f, 0.40f), new Color(0.26f, 0.22f, 0.16f), new Color(0.76f, 0.70f, 0.56f)),
        _ => ResolveHashedWoodPalette(materialId),
    };

    private static TilePalette ResolveHashedWoodPalette(string? materialId)
    {
        var offset = ResolvePaletteOffset(materialId, 0.10f);
        return new TilePalette(
            new Color(0.54f + offset, 0.36f + (offset * 0.45f), 0.18f - (offset * 0.10f)),
            new Color(0.25f + (offset * 0.20f), 0.16f + (offset * 0.10f), 0.08f),
            new Color(0.80f + (offset * 0.30f), 0.68f + (offset * 0.18f), 0.42f + (offset * 0.12f)));
    }

    private static TilePalette ResolveStoneItemPalette(string? materialId)
        => ResolveStoneWallPalette(materialId);

    private static TilePalette ResolveMetalItemPalette(string? materialId) => materialId switch
    {
        "iron" => new TilePalette(new Color(0.72f, 0.74f, 0.80f), new Color(0.46f, 0.48f, 0.54f), new Color(0.84f, 0.86f, 0.90f)),
        "copper" => new TilePalette(new Color(0.83f, 0.51f, 0.30f), new Color(0.52f, 0.28f, 0.15f), new Color(0.92f, 0.64f, 0.42f)),
        "tin" => new TilePalette(new Color(0.80f, 0.84f, 0.88f), new Color(0.52f, 0.56f, 0.60f), new Color(0.92f, 0.94f, 0.96f)),
        "silver" => new TilePalette(new Color(0.88f, 0.90f, 0.94f), new Color(0.58f, 0.60f, 0.66f), new Color(0.96f, 0.97f, 0.99f)),
        "gold" => new TilePalette(new Color(0.95f, 0.82f, 0.36f), new Color(0.62f, 0.46f, 0.14f), new Color(0.99f, 0.91f, 0.54f)),
        "coal" => new TilePalette(new Color(0.30f, 0.30f, 0.34f), new Color(0.14f, 0.14f, 0.16f), new Color(0.44f, 0.44f, 0.50f)),
        _ => ResolveHashedMetalPalette(materialId),
    };

    private static TilePalette ResolveHashedMetalPalette(string? materialId)
    {
        var offset = ResolvePaletteOffset(materialId, 0.08f);
        return new TilePalette(
            new Color(0.70f + (offset * 0.25f), 0.72f + (offset * 0.12f), 0.78f + (offset * 0.35f)),
            new Color(0.44f + (offset * 0.14f), 0.46f + (offset * 0.10f), 0.52f + (offset * 0.22f)),
            new Color(0.86f + (offset * 0.20f), 0.88f + (offset * 0.12f), 0.92f + (offset * 0.18f)));
    }

    private static TilePalette ResolveSeedItemPalette(string? materialId)
    {
        var offset = ResolvePaletteOffset(materialId, 0.07f);
        return new TilePalette(
            new Color(0.74f + (offset * 0.18f), 0.60f + (offset * 0.18f), 0.28f + (offset * 0.12f)),
            new Color(0.36f + (offset * 0.10f), 0.27f + (offset * 0.08f), 0.10f),
            new Color(0.88f + (offset * 0.10f), 0.76f + (offset * 0.12f), 0.42f + (offset * 0.08f)));
    }

    private static (Color Rock, Color Ore) ResolveOreChunkPalette(string? materialId)
    {
        var orePalette = ResolveMetalItemPalette(materialId);
        var rockPalette = string.Equals(materialId, "coal", StringComparison.OrdinalIgnoreCase)
            ? new TilePalette(new Color(0.16f, 0.16f, 0.18f), new Color(0.08f, 0.08f, 0.09f), new Color(0.36f, 0.36f, 0.40f))
            : ResolveStoneItemPalette(null);
        return (rockPalette.Base, orePalette.Base);
    }

    private static float ResolvePaletteOffset(string? materialId, float scale)
    {
        if (string.IsNullOrWhiteSpace(materialId))
            return 0f;

        var hash = StableHash(materialId);
        var normalized = ((hash & 255) / 255f) - 0.5f;
        return normalized * scale;
    }

    private static Texture2D MakeLog()
        => MakeLog(new Color(0.50f, 0.31f, 0.13f), new Color(0.80f, 0.68f, 0.44f), new Color(0.24f, 0.14f, 0.05f));

    private static Texture2D MakeLog(Color body, Color endCap, Color outline)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(10, 24, 44, 16), body);
        FillRect(image, new Rect2I(6, 24, 8, 16), endCap);
        FillRect(image, new Rect2I(50, 24, 8, 16), endCap);
        return CreateOutlinedTexture(image, new Color(outline.R, outline.G, outline.B, 0.95f));
    }

    private static Texture2D MakeBoulder(Color baseColor, Color highlight, Color shadow)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(16, 18, 30, 24), baseColor);
        FillRect(image, new Rect2I(12, 24, 10, 16), baseColor.Darkened(0.06f));
        FillRect(image, new Rect2I(40, 22, 12, 18), baseColor.Darkened(0.12f));
        FillRect(image, new Rect2I(20, 20, 18, 10), highlight);
        FillRect(image, new Rect2I(24, 32, 16, 8), baseColor.Lightened(0.06f));
        FillRect(image, new Rect2I(18, 40, 24, 4), shadow);
        FillRect(image, new Rect2I(28, 24, 2, 12), shadow.Lightened(0.08f));
        FillRect(image, new Rect2I(34, 28, 2, 8), shadow.Lightened(0.08f));
        return CreateOutlinedTexture(image, shadow.Darkened(0.12f));
    }

    private static Texture2D MakeOreChunk(Color rockColor, Color oreColor)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(14, 18, 34, 26), rockColor);
        FillRect(image, new Rect2I(18, 14, 20, 10), rockColor.Lightened(0.08f));
        FillRect(image, new Rect2I(20, 24, 6, 6), oreColor);
        FillRect(image, new Rect2I(32, 28, 8, 6), oreColor.Lightened(0.10f));
        FillRect(image, new Rect2I(26, 36, 6, 4), oreColor);
        Outline(image, rockColor.Darkened(0.25f), new Rect2I(14, 18, 34, 26));
        return CreateOutlinedTexture(image, rockColor.Darkened(0.35f));
    }

    private static Texture2D MakeIngot(Color top, Color side)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(16, 18, 30, 10), top);
        FillRect(image, new Rect2I(12, 28, 38, 12), top.Lightened(0.05f));
        FillRect(image, new Rect2I(16, 40, 30, 8), side);
        Outline(image, side.Darkened(0.15f), new Rect2I(12, 18, 38, 30));
        return CreateOutlinedTexture(image, side.Darkened(0.28f));
    }

    private static Texture2D MakeBarrel()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(20, 14, 24, 36), new Color(0.47f, 0.28f, 0.12f));
        FillRect(image, new Rect2I(18, 20, 28, 4), new Color(0.30f, 0.30f, 0.34f));
        FillRect(image, new Rect2I(18, 38, 28, 4), new Color(0.30f, 0.30f, 0.34f));
        return CreateOutlinedTexture(image, new Color(0.22f, 0.14f, 0.07f, 0.95f));
    }

    private static Texture2D MakeBucket()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(20, 24, 24, 20), new Color(0.53f, 0.35f, 0.18f));
        FillRect(image, new Rect2I(16, 20, 32, 4), new Color(0.32f, 0.32f, 0.36f));
        FillRect(image, new Rect2I(24, 14, 2, 8), new Color(0.72f, 0.72f, 0.76f));
        FillRect(image, new Rect2I(38, 14, 2, 8), new Color(0.72f, 0.72f, 0.76f));
        FillRect(image, new Rect2I(24, 12, 16, 2), new Color(0.72f, 0.72f, 0.76f));
        return CreateOutlinedTexture(image, new Color(0.23f, 0.15f, 0.08f, 0.95f));
    }

    private static Texture2D MakeBox()
    {
        // Flat-top wooden box with visible plank lines and a clasp
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Body
        FillRect(image, new Rect2I(14, 20, 36, 30), new Color(0.55f, 0.37f, 0.17f));
        // Lid top face (slightly lighter)
        FillRect(image, new Rect2I(14, 14, 36, 8), new Color(0.64f, 0.46f, 0.22f));
        // Lid hinge line
        FillRect(image, new Rect2I(14, 20, 36, 2), new Color(0.30f, 0.30f, 0.34f));
        // Vertical plank dividers on body
        FillRect(image, new Rect2I(26, 22, 2, 28), new Color(0.38f, 0.24f, 0.10f));
        FillRect(image, new Rect2I(38, 22, 2, 28), new Color(0.38f, 0.24f, 0.10f));
        // Horizontal strap
        FillRect(image, new Rect2I(14, 35, 36, 3), new Color(0.30f, 0.30f, 0.34f));
        // Clasp
        FillRect(image, new Rect2I(29, 18, 6, 5), new Color(0.72f, 0.62f, 0.22f));
        return CreateOutlinedTexture(image, new Color(0.22f, 0.13f, 0.06f, 0.95f));
    }

    private static Texture2D MakeCrate(Color body)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(18, 18, 28, 28), body);
        Outline(image, new Color(0, 0, 0, 0.2f), new Rect2I(18, 18, 28, 28));
        return CreateOutlinedTexture(image, body.Darkened(0.38f));
    }

    private static Texture2D MakePlank()
        => MakePlank(new Color(0.66f, 0.44f, 0.19f), new Color(0.72f, 0.50f, 0.23f), new Color(0.28f, 0.18f, 0.07f));

    private static Texture2D MakePlank(Color body, Color highlight, Color outline)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(12, 20, 40, 8), highlight);
        FillRect(image, new Rect2I(12, 32, 40, 8), body);
        return CreateOutlinedTexture(image, new Color(outline.R, outline.G, outline.B, 0.95f));
    }

    private static Texture2D MakeMeal()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(16, 34, 32, 8), new Color(0.64f, 0.42f, 0.22f));
        FillRect(image, new Rect2I(20, 24, 24, 10), new Color(0.83f, 0.72f, 0.58f));
        FillRect(image, new Rect2I(22, 18, 20, 8), new Color(0.45f, 0.65f, 0.22f));
        FillRect(image, new Rect2I(26, 22, 6, 4), new Color(0.77f, 0.35f, 0.14f));
        FillRect(image, new Rect2I(34, 20, 4, 4), new Color(0.92f, 0.82f, 0.34f));
        return CreateOutlinedTexture(image, new Color(0.28f, 0.18f, 0.09f, 0.95f));
    }

    private static Texture2D MakeDrink()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(22, 18, 20, 20), new Color(0.55f, 0.36f, 0.18f));
        FillRect(image, new Rect2I(24, 20, 16, 8), new Color(0.30f, 0.62f, 0.82f));
        FillRect(image, new Rect2I(40, 22, 6, 12), new Color(0.80f, 0.80f, 0.84f));
        FillRect(image, new Rect2I(30, 38, 4, 10), new Color(0.45f, 0.26f, 0.12f));
        FillRect(image, new Rect2I(26, 48, 12, 2), new Color(0.30f, 0.30f, 0.34f));
        return CreateOutlinedTexture(image, new Color(0.23f, 0.14f, 0.07f, 0.95f));
    }

    private static Texture2D MakeSeeds()
        => MakeSeeds(new Color(0.74f, 0.62f, 0.28f), new Color(0.86f, 0.74f, 0.42f), new Color(0.36f, 0.27f, 0.10f));

    private static Texture2D MakeSeeds(Color seed, Color highlight, Color outline)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(20, 30, 4, 6), seed);
        FillRect(image, new Rect2I(26, 24, 4, 6), highlight);
        FillRect(image, new Rect2I(32, 32, 4, 6), seed);
        FillRect(image, new Rect2I(38, 26, 4, 6), highlight);
        return CreateOutlinedTexture(image, new Color(outline.R, outline.G, outline.B, 0.95f));
    }

    private static Texture2D MakePlantBundle()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(24, 18, 6, 26), new Color(0.26f, 0.56f, 0.18f));
        FillRect(image, new Rect2I(30, 18, 6, 26), new Color(0.32f, 0.64f, 0.22f));
        FillRect(image, new Rect2I(36, 20, 6, 24), new Color(0.20f, 0.48f, 0.16f));
        FillRect(image, new Rect2I(22, 40, 24, 4), new Color(0.58f, 0.40f, 0.18f));
        return CreateOutlinedTexture(image, new Color(0.10f, 0.22f, 0.08f, 0.95f));
    }

    private static Texture2D MakeBerryCluster()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Stem
        FillRect(image, new Rect2I(30, 14, 4, 8), new Color(0.26f, 0.48f, 0.14f));
        // Berries — deep red-purple
        FillRect(image, new Rect2I(20, 20, 10, 10), new Color(0.58f, 0.12f, 0.26f));
        FillRect(image, new Rect2I(32, 18, 10, 10), new Color(0.68f, 0.14f, 0.30f));
        FillRect(image, new Rect2I(24, 30, 10, 10), new Color(0.62f, 0.10f, 0.22f));
        FillRect(image, new Rect2I(36, 28, 10, 10), new Color(0.72f, 0.16f, 0.34f));
        // Highlights
        FillRect(image, new Rect2I(22, 22, 4, 4), new Color(0.82f, 0.36f, 0.52f));
        FillRect(image, new Rect2I(34, 20, 4, 4), new Color(0.84f, 0.40f, 0.56f));
        return CreateOutlinedTexture(image, new Color(0.28f, 0.05f, 0.12f, 0.95f));
    }

    private static Texture2D MakeSunrootBulb()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Roots dangling
        FillRect(image, new Rect2I(22, 44, 4, 10), new Color(0.62f, 0.48f, 0.24f));
        FillRect(image, new Rect2I(30, 46, 4, 8),  new Color(0.66f, 0.52f, 0.28f));
        FillRect(image, new Rect2I(38, 44, 4, 10), new Color(0.62f, 0.48f, 0.24f));
        // Bulb body — warm golden-ochre
        FillRect(image, new Rect2I(18, 26, 28, 20), new Color(0.86f, 0.72f, 0.22f));
        FillRect(image, new Rect2I(14, 30, 10, 12), new Color(0.82f, 0.68f, 0.20f));
        FillRect(image, new Rect2I(40, 30, 10, 12), new Color(0.80f, 0.64f, 0.18f));
        // Highlight
        FillRect(image, new Rect2I(22, 28, 10, 8),  new Color(0.96f, 0.88f, 0.52f));
        // Shoot tip
        FillRect(image, new Rect2I(28, 18, 8, 10), new Color(0.40f, 0.64f, 0.18f));
        return CreateOutlinedTexture(image, new Color(0.40f, 0.28f, 0.08f, 0.95f));
    }

    private static Texture2D MakeStoneTuber()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Roots
        FillRect(image, new Rect2I(24, 46, 4, 8),  new Color(0.52f, 0.40f, 0.28f));
        FillRect(image, new Rect2I(36, 46, 4, 8),  new Color(0.52f, 0.40f, 0.28f));
        // Tuber body — grey-brown earthy
        FillRect(image, new Rect2I(16, 24, 32, 24), new Color(0.62f, 0.54f, 0.42f));
        FillRect(image, new Rect2I(12, 28, 10, 16), new Color(0.58f, 0.50f, 0.38f));
        FillRect(image, new Rect2I(42, 28, 10, 16), new Color(0.56f, 0.48f, 0.36f));
        // Highlight & eye spots
        FillRect(image, new Rect2I(20, 26, 10, 8),  new Color(0.78f, 0.72f, 0.60f));
        FillRect(image, new Rect2I(26, 34, 4, 4),   new Color(0.42f, 0.32f, 0.22f));
        FillRect(image, new Rect2I(36, 30, 4, 4),   new Color(0.42f, 0.32f, 0.22f));
        return CreateOutlinedTexture(image, new Color(0.30f, 0.22f, 0.14f, 0.95f));
    }

    private static Texture2D MakeMarshReedShoot()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Three bundled shoots
        FillRect(image, new Rect2I(20, 16, 6, 36), new Color(0.42f, 0.66f, 0.28f));
        FillRect(image, new Rect2I(29, 12, 6, 40), new Color(0.48f, 0.72f, 0.32f));
        FillRect(image, new Rect2I(38, 18, 6, 32), new Color(0.40f, 0.62f, 0.26f));
        // Binding band
        FillRect(image, new Rect2I(18, 44, 28, 4), new Color(0.64f, 0.50f, 0.22f));
        // Tip tufts — lighter yellow-green
        FillRect(image, new Rect2I(18, 10, 8, 8),  new Color(0.72f, 0.84f, 0.36f));
        FillRect(image, new Rect2I(27, 6,  8, 8),  new Color(0.76f, 0.88f, 0.40f));
        FillRect(image, new Rect2I(36, 12, 8, 8),  new Color(0.70f, 0.82f, 0.34f));
        return CreateOutlinedTexture(image, new Color(0.18f, 0.28f, 0.10f, 0.95f));
    }

    private static Texture2D MakeApple()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Stem
        FillRect(image, new Rect2I(30, 10, 4, 8), new Color(0.36f, 0.22f, 0.10f));
        // Leaf
        FillRect(image, new Rect2I(34, 10, 10, 6), new Color(0.28f, 0.58f, 0.18f));
        // Apple body — rich red
        FillRect(image, new Rect2I(16, 18, 32, 30), new Color(0.82f, 0.14f, 0.10f));
        FillRect(image, new Rect2I(12, 24, 10, 18), new Color(0.76f, 0.12f, 0.10f));
        FillRect(image, new Rect2I(42, 24, 10, 18), new Color(0.74f, 0.10f, 0.08f));
        FillRect(image, new Rect2I(18, 46, 28, 6),  new Color(0.72f, 0.12f, 0.08f));
        // Highlight
        FillRect(image, new Rect2I(20, 20, 10, 10), new Color(0.96f, 0.52f, 0.48f));
        // Bottom cleft
        FillRect(image, new Rect2I(30, 48, 4, 4),   new Color(0.50f, 0.08f, 0.06f));
        return CreateOutlinedTexture(image, new Color(0.38f, 0.06f, 0.04f, 0.95f));
    }

    private static Texture2D MakeFig()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        // Stem + leaf
        FillRect(image, new Rect2I(30, 10, 4, 8),  new Color(0.34f, 0.20f, 0.10f));
        FillRect(image, new Rect2I(26, 8, 12, 6),  new Color(0.28f, 0.54f, 0.16f));
        // Fig body — deep purple-brown
        FillRect(image, new Rect2I(18, 16, 28, 32), new Color(0.42f, 0.20f, 0.36f));
        FillRect(image, new Rect2I(14, 22, 10, 20), new Color(0.38f, 0.18f, 0.32f));
        FillRect(image, new Rect2I(42, 22, 10, 20), new Color(0.36f, 0.16f, 0.30f));
        FillRect(image, new Rect2I(20, 46, 24, 6),  new Color(0.50f, 0.26f, 0.44f));
        // Highlight
        FillRect(image, new Rect2I(22, 18, 8, 8),   new Color(0.64f, 0.38f, 0.58f));
        // Eye (ostiole)
        FillRect(image, new Rect2I(28, 46, 8, 4),   new Color(0.22f, 0.10f, 0.18f));
        return CreateOutlinedTexture(image, new Color(0.20f, 0.08f, 0.16f, 0.95f));
    }

    private static Texture2D MakeHide()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(16, 18, 30, 26), new Color(0.63f, 0.47f, 0.28f));
        FillRect(image, new Rect2I(12, 24, 10, 12), new Color(0.71f, 0.56f, 0.34f));
        FillRect(image, new Rect2I(40, 22, 10, 14), new Color(0.56f, 0.40f, 0.24f));
        FillRect(image, new Rect2I(22, 16, 16, 6), new Color(0.76f, 0.62f, 0.40f));
        Outline(image, new Color(0.34f, 0.24f, 0.12f), new Rect2I(12, 16, 38, 28));
        return CreateOutlinedTexture(image, new Color(0.30f, 0.21f, 0.11f, 0.95f));
    }

    private static Texture2D MakeCloth()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(16, 20, 30, 20), new Color(0.82f, 0.78f, 0.70f));
        FillRect(image, new Rect2I(20, 24, 22, 12), new Color(0.90f, 0.87f, 0.80f));
        FillRect(image, new Rect2I(18, 38, 24, 4), new Color(0.70f, 0.66f, 0.58f));
        return CreateOutlinedTexture(image, new Color(0.46f, 0.42f, 0.36f, 0.95f));
    }

    private static Texture2D MakeBone()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(22, 28, 20, 8), new Color(0.88f, 0.86f, 0.76f));
        FillRect(image, new Rect2I(16, 22, 8, 8), new Color(0.92f, 0.90f, 0.82f));
        FillRect(image, new Rect2I(16, 34, 8, 8), new Color(0.92f, 0.90f, 0.82f));
        FillRect(image, new Rect2I(40, 22, 8, 8), new Color(0.92f, 0.90f, 0.82f));
        FillRect(image, new Rect2I(40, 34, 8, 8), new Color(0.92f, 0.90f, 0.82f));
        return CreateOutlinedTexture(image, new Color(0.54f, 0.52f, 0.45f, 0.95f));
    }

    private static Texture2D MakeCorpse()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        var bone = new Color(0.86f, 0.84f, 0.74f);
        var boneHighlight = new Color(0.94f, 0.92f, 0.84f);
        var boneShadow = new Color(0.67f, 0.64f, 0.56f);
        var cavity = new Color(0.14f, 0.12f, 0.10f, 0.92f);

        // Skull
        FillRect(image, new Rect2I(21, 12, 22, 16), bone);
        FillRect(image, new Rect2I(24, 10, 16, 4), boneHighlight);
        FillRect(image, new Rect2I(23, 26, 18, 4), boneShadow);
        FillRect(image, new Rect2I(27, 17, 4, 4), cavity);
        FillRect(image, new Rect2I(33, 17, 4, 4), cavity);
        FillRect(image, new Rect2I(31, 22, 2, 3), cavity);

        // Jaw + teeth band
        FillRect(image, new Rect2I(23, 30, 18, 4), bone);
        FillRect(image, new Rect2I(24, 34, 16, 3), boneShadow);
        FillRect(image, new Rect2I(25, 31, 14, 1), boneHighlight);

        // Spine and ribs
        FillRect(image, new Rect2I(30, 36, 4, 16), boneShadow);
        FillRect(image, new Rect2I(24, 39, 16, 3), bone);
        FillRect(image, new Rect2I(22, 43, 20, 3), bone);
        FillRect(image, new Rect2I(24, 47, 16, 3), bone);
        FillRect(image, new Rect2I(26, 51, 12, 3), boneShadow);

        // Scattered limb bones
        FillRect(image, new Rect2I(12, 38, 10, 3), bone);
        FillRect(image, new Rect2I(42, 40, 10, 3), bone);
        FillRect(image, new Rect2I(14, 46, 9, 3), boneShadow);
        FillRect(image, new Rect2I(41, 48, 9, 3), boneShadow);
        FillRect(image, new Rect2I(18, 54, 8, 3), bone);
        FillRect(image, new Rect2I(38, 55, 8, 3), bone);

        return CreateOutlinedTexture(image, new Color(0.32f, 0.29f, 0.23f, 0.95f));
    }

    private static Texture2D MakeBed()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(14, 24, 36, 20), new Color(0.58f, 0.39f, 0.21f));
        FillRect(image, new Rect2I(18, 20, 12, 10), new Color(0.88f, 0.84f, 0.78f));
        FillRect(image, new Rect2I(30, 28, 16, 10), new Color(0.54f, 0.67f, 0.82f));
        FillRect(image, new Rect2I(16, 44, 4, 8), new Color(0.36f, 0.22f, 0.12f));
        FillRect(image, new Rect2I(44, 44, 4, 8), new Color(0.36f, 0.22f, 0.12f));
        return CreateOutlinedTexture(image, new Color(0.24f, 0.15f, 0.08f, 0.95f));
    }

    private static Texture2D MakeTable()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(14, 20, 36, 10), new Color(0.60f, 0.41f, 0.22f));
        FillRect(image, new Rect2I(18, 30, 4, 16), new Color(0.42f, 0.28f, 0.15f));
        FillRect(image, new Rect2I(42, 30, 4, 16), new Color(0.42f, 0.28f, 0.15f));
        return CreateOutlinedTexture(image, new Color(0.22f, 0.14f, 0.08f, 0.95f));
    }

    private static Texture2D MakeChair()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(24, 16, 14, 12), new Color(0.58f, 0.39f, 0.21f));
        FillRect(image, new Rect2I(22, 30, 18, 8), new Color(0.66f, 0.45f, 0.24f));
        FillRect(image, new Rect2I(24, 38, 4, 14), new Color(0.40f, 0.26f, 0.14f));
        FillRect(image, new Rect2I(34, 38, 4, 14), new Color(0.40f, 0.26f, 0.14f));
        return CreateOutlinedTexture(image, new Color(0.22f, 0.14f, 0.08f, 0.95f));
    }

    private static Texture2D MakeWorkshop(Color body, Color trim, Color accent)
    {
        var image = NewImage();
        FillRect(image, new Rect2I(0, 0, Size, Size), new Color(0, 0, 0, 0));
        FillRect(image, new Rect2I(8, 14, 48, 36), body);
        FillRect(image, new Rect2I(8, 12, 48, 6), trim);
        FillRect(image, new Rect2I(12, 20, 16, 12), accent);
        FillRect(image, new Rect2I(36, 20, 12, 22), trim.Darkened(0.1f));
        FillRect(image, new Rect2I(16, 36, 28, 8), accent.Darkened(0.18f));
        Outline(image, trim, new Rect2I(8, 14, 48, 36));
        return CreateOutlinedTexture(image, trim.Darkened(0.32f));
    }

    private static Texture2D MakePickaxeIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(28, 18, 8, 30), new Color(0.57f, 0.38f, 0.18f));
        FillRect(image, new Rect2I(16, 14, 32, 8), new Color(0.70f, 0.74f, 0.78f));
        FillRect(image, new Rect2I(12, 18, 16, 6), new Color(0.84f, 0.88f, 0.92f));
        FillRect(image, new Rect2I(36, 18, 16, 6), new Color(0.84f, 0.88f, 0.92f));
        FillRect(image, new Rect2I(24, 46, 16, 6), new Color(0.36f, 0.24f, 0.10f));
        return CreateOutlinedTexture(image, new Color(0.20f, 0.14f, 0.08f, 0.95f));
    }

    private static Texture2D MakeHandIcon()
    {
        var image = NewImage();
        var skin = new Color(0.90f, 0.78f, 0.62f);
        var shadow = new Color(0.72f, 0.56f, 0.40f);
        FillRect(image, new Rect2I(18, 26, 20, 18), skin);
        FillRect(image, new Rect2I(18, 18, 4, 14), skin);
        FillRect(image, new Rect2I(24, 14, 4, 18), skin);
        FillRect(image, new Rect2I(30, 12, 4, 20), skin);
        FillRect(image, new Rect2I(36, 16, 4, 16), skin);
        FillRect(image, new Rect2I(38, 28, 8, 12), skin);
        FillRect(image, new Rect2I(22, 42, 12, 8), shadow);
        return CreateOutlinedTexture(image, new Color(0.36f, 0.22f, 0.12f, 0.95f));
    }

    private static Texture2D MakeCancelIcon()
    {
        var image = NewImage();
        DrawLineRect(image, 14, 14, 36, 8, new Color(0.88f, 0.24f, 0.20f));
        DrawLineRect(image, 14, 42, 36, 8, new Color(0.88f, 0.24f, 0.20f));
        DrawLineRect(image, 14, 22, 8, 20, new Color(0.88f, 0.24f, 0.20f));
        DrawLineRect(image, 42, 22, 8, 20, new Color(0.88f, 0.24f, 0.20f));
        return CreateOutlinedTexture(image, new Color(0.40f, 0.08f, 0.08f, 0.95f));
    }

    private static Texture2D MakeZoneIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(14, 14, 36, 36), new Color(0.22f, 0.64f, 0.76f, 0.18f));
        Outline(image, new Color(0.22f, 0.64f, 0.76f), new Rect2I(14, 14, 36, 36));
        FillRect(image, new Rect2I(22, 22, 8, 8), new Color(0.30f, 0.78f, 0.88f));
        FillRect(image, new Rect2I(34, 22, 8, 8), new Color(0.30f, 0.78f, 0.88f));
        FillRect(image, new Rect2I(22, 34, 8, 8), new Color(0.30f, 0.78f, 0.88f));
        FillRect(image, new Rect2I(34, 34, 8, 8), new Color(0.30f, 0.78f, 0.88f));
        return CreateOutlinedTexture(image, new Color(0.08f, 0.28f, 0.32f, 0.95f));
    }

    private static Texture2D MakeBuildIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(26, 18, 10, 28), new Color(0.56f, 0.36f, 0.18f));
        FillRect(image, new Rect2I(16, 12, 30, 10), new Color(0.70f, 0.72f, 0.78f));
        FillRect(image, new Rect2I(38, 18, 8, 10), new Color(0.84f, 0.86f, 0.92f));
        FillRect(image, new Rect2I(20, 44, 24, 6), new Color(0.46f, 0.30f, 0.14f));
        return CreateOutlinedTexture(image, new Color(0.20f, 0.14f, 0.08f, 0.95f));
    }

    private static Texture2D MakeSpeedIcon(int arrows)
    {
        var image = NewImage();
        var color = new Color(0.96f, 0.72f, 0.18f);
        for (int index = 0; index < arrows; index++)
        {
            var offset = index * 10;
            FillRect(image, new Rect2I(14 + offset, 22, 12, 20), color);
            FillRect(image, new Rect2I(24 + offset, 18, 8, 8), color);
            FillRect(image, new Rect2I(24 + offset, 38, 8, 8), color);
        }

        return CreateOutlinedTexture(image, new Color(0.44f, 0.24f, 0.04f, 0.95f));
    }

    private static Texture2D MakePauseIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(18, 14, 10, 36), new Color(0.80f, 0.82f, 0.88f));
        FillRect(image, new Rect2I(36, 14, 10, 36), new Color(0.80f, 0.82f, 0.88f));
        return CreateOutlinedTexture(image, new Color(0.24f, 0.26f, 0.32f, 0.95f));
    }

    private static Texture2D MakeBookIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(14, 12, 36, 40), new Color(0.55f, 0.28f, 0.18f));
        FillRect(image, new Rect2I(18, 14, 28, 36), new Color(0.92f, 0.90f, 0.82f));
        FillRect(image, new Rect2I(14, 12, 4, 40), new Color(0.42f, 0.20f, 0.12f));
        FillRect(image, new Rect2I(22, 20, 20, 2), new Color(0.60f, 0.60f, 0.55f));
        FillRect(image, new Rect2I(22, 26, 18, 2), new Color(0.60f, 0.60f, 0.55f));
        FillRect(image, new Rect2I(22, 32, 22, 2), new Color(0.60f, 0.60f, 0.55f));
        FillRect(image, new Rect2I(22, 38, 16, 2), new Color(0.60f, 0.60f, 0.55f));
        return CreateOutlinedTexture(image, new Color(0.28f, 0.14f, 0.08f, 0.95f));
    }

    private static Texture2D MakeFortressIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(16, 18, 32, 30), new Color(0.60f, 0.62f, 0.68f));
        FillRect(image, new Rect2I(12, 14, 10, 16), new Color(0.72f, 0.74f, 0.80f));
        FillRect(image, new Rect2I(42, 14, 10, 16), new Color(0.72f, 0.74f, 0.80f));
        FillRect(image, new Rect2I(28, 34, 8, 14), new Color(0.34f, 0.22f, 0.12f));
        FillRect(image, new Rect2I(22, 26, 6, 6), new Color(0.26f, 0.38f, 0.62f));
        FillRect(image, new Rect2I(36, 26, 6, 6), new Color(0.26f, 0.38f, 0.62f));
        return CreateOutlinedTexture(image, new Color(0.22f, 0.24f, 0.30f, 0.95f));
    }

    private static Texture2D MakeCalendarIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(12, 14, 40, 36), new Color(0.92f, 0.88f, 0.76f));
        FillRect(image, new Rect2I(12, 14, 40, 10), new Color(0.84f, 0.32f, 0.26f));
        FillRect(image, new Rect2I(20, 10, 6, 12), new Color(0.70f, 0.72f, 0.78f));
        FillRect(image, new Rect2I(38, 10, 6, 12), new Color(0.70f, 0.72f, 0.78f));
        FillRect(image, new Rect2I(20, 30, 8, 8), new Color(0.96f, 0.70f, 0.18f));
        FillRect(image, new Rect2I(34, 30, 10, 8), new Color(0.62f, 0.64f, 0.70f));
        return CreateOutlinedTexture(image, new Color(0.28f, 0.16f, 0.12f, 0.95f));
    }

    private static Texture2D MakeMigrationIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(18, 14, 10, 16), new Color(0.40f, 0.24f, 0.12f));
        FillRect(image, new Rect2I(18, 28, 18, 10), new Color(0.56f, 0.34f, 0.18f));
        FillRect(image, new Rect2I(28, 34, 8, 12), new Color(0.56f, 0.34f, 0.18f));
        FillRect(image, new Rect2I(32, 38, 14, 8), new Color(0.72f, 0.46f, 0.24f));
        FillRect(image, new Rect2I(40, 18, 8, 18), new Color(0.44f, 0.62f, 0.90f));
        return CreateOutlinedTexture(image, new Color(0.24f, 0.16f, 0.08f, 0.95f));
    }

    private static Texture2D MakeBannerIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(18, 12, 6, 40), new Color(0.54f, 0.38f, 0.18f));
        FillRect(image, new Rect2I(24, 14, 24, 18), new Color(0.28f, 0.50f, 0.82f));
        FillRect(image, new Rect2I(24, 30, 18, 12), new Color(0.22f, 0.40f, 0.68f));
        FillRect(image, new Rect2I(30, 20, 6, 6), new Color(0.92f, 0.82f, 0.30f));
        return CreateOutlinedTexture(image, new Color(0.14f, 0.18f, 0.28f, 0.95f));
    }

    private static Texture2D MakeThreatIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(24, 12, 16, 26), new Color(0.82f, 0.20f, 0.18f));
        FillRect(image, new Rect2I(28, 40, 8, 8), new Color(0.96f, 0.78f, 0.26f));
        FillRect(image, new Rect2I(30, 18, 4, 14), new Color(0.98f, 0.92f, 0.80f));
        FillRect(image, new Rect2I(30, 36, 4, 4), new Color(0.98f, 0.92f, 0.80f));
        return CreateOutlinedTexture(image, new Color(0.34f, 0.08f, 0.08f, 0.95f));
    }

    private static Texture2D MakeMoodIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(14, 16, 36, 30), new Color(0.96f, 0.82f, 0.40f));
        FillRect(image, new Rect2I(20, 24, 6, 6), new Color(0.18f, 0.18f, 0.18f));
        FillRect(image, new Rect2I(38, 24, 6, 6), new Color(0.18f, 0.18f, 0.18f));
        FillRect(image, new Rect2I(22, 38, 20, 4), new Color(0.58f, 0.22f, 0.22f));
        return CreateOutlinedTexture(image, new Color(0.38f, 0.22f, 0.10f, 0.95f));
    }

    private static Texture2D MakeNeedIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(24, 12, 12, 12), new Color(0.42f, 0.72f, 0.96f));
        FillRect(image, new Rect2I(20, 22, 20, 18), new Color(0.30f, 0.60f, 0.90f));
        FillRect(image, new Rect2I(18, 40, 24, 6), new Color(0.54f, 0.34f, 0.18f));
        FillRect(image, new Rect2I(24, 34, 12, 6), new Color(0.88f, 0.70f, 0.30f));
        return CreateOutlinedTexture(image, new Color(0.12f, 0.24f, 0.40f, 0.95f));
    }

    private static Texture2D MakeDeathIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(18, 16, 28, 22), new Color(0.84f, 0.84f, 0.84f));
        FillRect(image, new Rect2I(22, 38, 20, 12), new Color(0.84f, 0.84f, 0.84f));
        FillRect(image, new Rect2I(24, 24, 6, 6), new Color(0.18f, 0.18f, 0.20f));
        FillRect(image, new Rect2I(34, 24, 6, 6), new Color(0.18f, 0.18f, 0.20f));
        FillRect(image, new Rect2I(28, 40, 8, 4), new Color(0.18f, 0.18f, 0.20f));
        return CreateOutlinedTexture(image, new Color(0.22f, 0.16f, 0.16f, 0.95f));
    }

    private static Texture2D MakeCombatIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(18, 18, 8, 24), new Color(0.78f, 0.80f, 0.86f));
        FillRect(image, new Rect2I(38, 18, 8, 24), new Color(0.78f, 0.80f, 0.86f));
        FillRect(image, new Rect2I(22, 14, 8, 10), new Color(0.92f, 0.94f, 0.98f));
        FillRect(image, new Rect2I(34, 14, 8, 10), new Color(0.92f, 0.94f, 0.98f));
        FillRect(image, new Rect2I(16, 36, 12, 8), new Color(0.44f, 0.26f, 0.16f));
        FillRect(image, new Rect2I(36, 36, 12, 8), new Color(0.44f, 0.26f, 0.16f));
        return CreateOutlinedTexture(image, new Color(0.16f, 0.18f, 0.24f, 0.95f));
    }

    private static Texture2D MakeFloodIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(10, 34, 44, 6), new Color(0.22f, 0.56f, 0.92f));
        FillRect(image, new Rect2I(14, 26, 12, 6), new Color(0.34f, 0.68f, 0.98f));
        FillRect(image, new Rect2I(30, 22, 12, 6), new Color(0.34f, 0.68f, 0.98f));
        FillRect(image, new Rect2I(40, 28, 10, 6), new Color(0.34f, 0.68f, 0.98f));
        FillRect(image, new Rect2I(18, 40, 14, 4), new Color(0.18f, 0.44f, 0.76f));
        FillRect(image, new Rect2I(36, 40, 12, 4), new Color(0.18f, 0.44f, 0.76f));
        return CreateOutlinedTexture(image, new Color(0.10f, 0.22f, 0.38f, 0.95f));
    }

    private static Texture2D MakeWildlifeIcon()
    {
        var image = NewImage();
        FillRect(image, new Rect2I(18, 32, 28, 14), new Color(0.60f, 0.42f, 0.26f));
        FillRect(image, new Rect2I(16, 20, 8, 8), new Color(0.60f, 0.42f, 0.26f));
        FillRect(image, new Rect2I(28, 14, 8, 8), new Color(0.60f, 0.42f, 0.26f));
        FillRect(image, new Rect2I(40, 20, 8, 8), new Color(0.60f, 0.42f, 0.26f));
        return CreateOutlinedTexture(image, new Color(0.24f, 0.16f, 0.08f, 0.95f));
    }

    private static void DrawLineRect(Image image, int x, int y, int width, int height, Color color)
    {
        for (int index = 0; index < width; index++)
        {
            var topY = y + (index * height / Math.Max(1, width));
            var bottomY = y + height - 1 - (index * height / Math.Max(1, width));
            if (topY >= 0 && topY < Size)
                image.SetPixel(x + index, topY, color);
            if (bottomY >= 0 && bottomY < Size)
                image.SetPixel(x + index, bottomY, color);
        }
    }

    private static Image NewImage()
    {
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        return image;
    }

    private static void FillRect(Image image, Rect2I rect, Color color)
    {
        image.FillRect(rect, color);
    }

    private static void BlendRect(Image image, Rect2I rect, Color color, float strength)
    {
        var clampedStrength = Math.Clamp(strength, 0f, 1f);
        if (clampedStrength <= 0f)
            return;

        var minX = Math.Max(0, rect.Position.X);
        var minY = Math.Max(0, rect.Position.Y);
        var maxX = Math.Min(Size - 1, rect.End.X - 1);
        var maxY = Math.Min(Size - 1, rect.End.Y - 1);
        if (minX > maxX || minY > maxY)
            return;

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var current = image.GetPixel(x, y);
            image.SetPixel(x, y, BlendColor(current, color, clampedStrength));
        }
    }

    private static Color BlendColor(Color from, Color to, float strength)
    {
        var t = Math.Clamp(strength, 0f, 1f);
        return new Color(
            Mathf.Lerp(from.R, to.R, t),
            Mathf.Lerp(from.G, to.G, t),
            Mathf.Lerp(from.B, to.B, t),
            1f);
    }

    private static Texture2D CreateOutlinedTexture(Image image, Color outlineColor)
    {
        OutlineOpaqueSilhouette(image, outlineColor, DefaultOutlineThickness);
        return ImageTexture.CreateFromImage(image);
    }

    private static void OutlineOpaqueSilhouette(Image image, Color color, int thickness)
    {
        var clampedThickness = Math.Max(1, thickness);
        for (int pass = 0; pass < clampedThickness; pass++)
        {
            var width = image.GetWidth();
            var height = image.GetHeight();
            var opaqueMask = new bool[width, height];

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                opaqueMask[x, y] = image.GetPixel(x, y).A > 0.01f;

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (opaqueMask[x, y] || !TouchesOpaqueNeighbor(opaqueMask, width, height, x, y))
                    continue;

                image.SetPixel(x, y, color);
            }
        }
    }

    private static bool TouchesOpaqueNeighbor(bool[,] opaqueMask, int width, int height, int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0)
                continue;

            var sampleX = x + dx;
            var sampleY = y + dy;
            if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
                continue;

            if (opaqueMask[sampleX, sampleY])
                return true;
        }

        return false;
    }

    private static void Outline(Image image, Color color, Rect2I? rect = null)
    {
        Outline(image, color, DefaultOutlineThickness, rect);
    }

    private static void Outline(Image image, Color color, int thickness, Rect2I? rect = null)
    {
        var clampedThickness = Math.Max(1, thickness);
        var area = rect ?? new Rect2I(0, 0, Size, Size);

        for (int layer = 0; layer < clampedThickness; layer++)
        {
            var left = area.Position.X + layer;
            var right = area.End.X - 1 - layer;
            var top = area.Position.Y + layer;
            var bottom = area.End.Y - 1 - layer;

            if (left > right || top > bottom)
                break;

            for (int x = left; x <= right; x++)
            {
                image.SetPixel(x, top, color);
                image.SetPixel(x, bottom, color);
            }

            for (int y = top; y <= bottom; y++)
            {
                image.SetPixel(left, y, color);
                image.SetPixel(right, y, color);
            }
        }
    }
}

public static class UiIconIds
{
    public const string Pickaxe = "pickaxe";
    public const string Hand = "hand";
    public const string Cancel = "cancel";
    public const string Zone = "zone";
    public const string Build = "build";
    public const string Speed1 = "speed_1";
    public const string Speed3 = "speed_3";
    public const string Speed5 = "speed_5";
    public const string Pause = "pause";
    public const string Book = "book";
    public const string Fortress = "fortress";
    public const string Calendar = "calendar";
    public const string Migration = "migration";
    public const string Banner = "banner";
    public const string Threat = "threat";
    public const string Mood = "mood";
    public const string Need = "need";
    public const string Death = "death";
    public const string Combat = "combat";
    public const string Flood = "flood";
    public const string Wildlife = "wildlife";
}
