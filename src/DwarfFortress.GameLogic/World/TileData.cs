using System;

namespace DwarfFortress.GameLogic.World;

/// <summary>Fluid types that can occupy tile fluid slots.</summary>
public enum FluidType : byte
{
    None  = 0,
    Water = 1,
    Magma = 2,
}

/// <summary>
/// Runtime state of a single tile in the world.
/// Kept small (struct) — millions of these exist in the world map.
/// </summary>
public struct TileData
{
    // ── Static definition ──────────────────────────────────────────────────
    /// <summary>References TileDef.Id (e.g. "stone_wall", "stone_floor").</summary>
    public string TileDefId;

    /// <summary>References MaterialDef.Id for the dominant material (e.g. "granite").</summary>
    public string? MaterialId;

    /// <summary>
    /// Optional tree species identifier for tree tiles (e.g. "oak", "pine").
    /// Null for non-tree tiles.
    /// </summary>
    public string? TreeSpeciesId;

    /// <summary>
    /// Optional plant definition identifier for wild vegetation or tree-borne canopy fruit.
    /// Null means the tile has no simulated plant growth state.
    /// </summary>
    public string? PlantDefId;

    /// <summary>
    /// Growth stage for PlantDefId. See <see cref="PlantGrowthStages"/>.
    /// </summary>
    public byte PlantGrowthStage;

    /// <summary>
    /// Seconds accumulated toward the next plant growth or yield transition.
    /// </summary>
    public float PlantGrowthProgressSeconds;

    /// <summary>
    /// 0 = no harvestable yield, 1 = mature or fruit-bearing yield present.
    /// </summary>
    public byte PlantYieldLevel;

    /// <summary>
    /// 0 = no exposed seeds, 1 = seed stage or recent seeded state.
    /// </summary>
    public byte PlantSeedLevel;

    /// <summary>
    /// Optional ore item definition ID embedded in this tile (e.g. "iron_ore").
    /// Null means the tile is plain rock.
    /// </summary>
    public string? OreItemDefId;

    /// <summary>
    /// True when this tile belongs to a water-bearing aquifer layer.
    /// Digging this tile should trigger immediate water seep.
    /// </summary>
    public bool IsAquifer;

    // ── Fluid state ────────────────────────────────────────────────────────
    public FluidType FluidType;

    /// <summary>Fluid pressure level: 0 = empty, 7 = full.</summary>
    public byte FluidLevel;

    /// <summary>
    /// Specific material ID of the fluid occupying this tile (e.g. "water", "beer", "magma",
    /// "lye_water"). Generic water has no entry (null). Used by ContaminationSystem so that
    /// fluids with interesting materials leave coatings and can be ingested.
    /// </summary>
    public string? FluidMaterialId;

    // ── Coating (dried/residual material on tile surface) ─────────────────

    /// <summary>
    /// Material ID of any coating left on the tile surface (e.g. "beer" dried after a spill,
    /// "mud", "blood"). Null = clean tile. Set by ContaminationSystem when fluid drains to 0.
    /// </summary>
    public string? CoatingMaterialId;

    /// <summary>
    /// How much coating remains: 0 = none, 1 = fully coated.
    /// Erodes as entities walk through and pick it up.
    /// </summary>
    public float CoatingAmount;

    // ── Designation & construction flags ──────────────────────────────────
    /// <summary>
    /// True when this tile has a pending designation (mine, cut, etc.).
    /// A Job will be created and this flag cleared when the job completes.
    /// </summary>
    public bool IsDesignated;

    /// <summary>True when a building is under construction on this tile.</summary>
    public bool IsUnderConstruction;

    // ── Derived ────────────────────────────────────────────────────────────

    /// <summary>
    /// True when a creature can walk through this tile without obstacle rules.
    /// Passability is derived from TileDef but checked here for hot-path speed.
    /// </summary>
    public bool IsPassable;

    public readonly bool HasFluid
        => FluidType != FluidType.None && FluidLevel > 0;

    public readonly bool HasPlant
        => !string.IsNullOrWhiteSpace(PlantDefId);

    public static TileData Empty => new()
    {
        TileDefId = TileDefIds.Empty,
        IsPassable = true,
    };

    public override readonly string ToString()
        => $"{TileDefId}({MaterialId ?? "-"}) fluid={FluidType}:{FluidLevel}";
}

public static class PlantGrowthStages
{
    public const byte Seed = 0;
    public const byte Sprout = 1;
    public const byte Young = 2;
    public const byte Mature = 3;
}

/// <summary>String constants for tile definition IDs. No magic strings in simulation code.</summary>
public static class TileDefIds
{
    public const string Empty        = "empty";
    public const string StoneFloor   = "stone_floor";
    public const string StoneWall    = "stone_wall";
    public const string Sand         = "sand";
    public const string Mud          = "mud";
    public const string Snow         = "snow";
    public const string GraniteWall  = StoneWall;
    public const string LimestoneWall= StoneWall;
    public const string SandstoneWall= StoneWall;
    public const string BasaltWall   = StoneWall;
    public const string ShaleWall    = StoneWall;
    public const string SlateWall    = StoneWall;
    public const string MarbleWall   = StoneWall;
    public const string Ramp         = "ramp";
    public const string Staircase    = "staircase";
    public const string Tree         = "tree";
    public const string Grass        = "grass";
    public const string Soil         = "soil";
    public const string SoilWall     = "soil_wall";
    public const string Water        = "water_tile";
    public const string Magma        = "magma_tile";
    public const string Obsidian     = "obsidian_floor";
    public const string WoodFloor    = "wood_floor";
    public const string StoneBrick   = "stone_brick_floor";
}
