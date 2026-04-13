namespace DwarfFortress.GameLogic.Core;

// ─────────────────────────────────────────────────────────────────────────────
// All player-originated commands are plain C# records in GameLogic.
// No Godot types appear here. Input handlers in the Godot project translate
// player gestures into these records and pass them to CommandDispatcher.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Marker interface for all player commands.</summary>
public interface ICommand { }

// ── Designation ──────────────────────────────────────────────────────────────

/// <summary>Designate a rectangular box of tiles for mining.</summary>
public record DesignateMineCommand(Vec3i From, Vec3i To) : ICommand;

/// <summary>Designate a rectangular box of tiles for tree-cutting.</summary>
public record DesignateCutTreesCommand(Vec3i From, Vec3i To) : ICommand;

/// <summary>Designate a rectangular box of tiles for plant harvesting.</summary>
public record DesignateHarvestCommand(Vec3i From, Vec3i To) : ICommand;

/// <summary>Cancel a designation on a rectangular area.</summary>
public record CancelDesignationCommand(Vec3i From, Vec3i To) : ICommand;

// ── Construction ─────────────────────────────────────────────────────────────

/// <summary>Place a building of the given definition at a world position.</summary>
public record PlaceBuildingCommand(string BuildingDefId, Vec3i Origin, Data.Defs.BuildingRotation Rotation = Data.Defs.BuildingRotation.None) : ICommand;

/// <summary>Deconstruct a building at a world position.</summary>
public record DeconstructBuildingCommand(Vec3i Origin) : ICommand;

// ── Stockpiles ────────────────────────────────────────────────────────────────

/// <summary>Define a new stockpile zone over a rectangular area.</summary>
public record CreateStockpileCommand(Vec3i From, Vec3i To, string[] AcceptedTags, int OwnerBuildingId = -1) : ICommand;

/// <summary>Remove a stockpile zone.</summary>
public record RemoveStockpileCommand(int StockpileId) : ICommand;

// ── Production ───────────────────────────────────────────────────────────────

/// <summary>Queue a production order on a workshop.</summary>
public record SetProductionOrderCommand(int WorkshopEntityId, string RecipeDefId, int Quantity) : ICommand;

/// <summary>Cancel a pending or active production order.</summary>
public record CancelProductionOrderCommand(int WorkshopEntityId, int OrderIndex) : ICommand;

// ── Labor ────────────────────────────────────────────────────────────────────

/// <summary>Enable or disable a specific labor for a dwarf.</summary>
public record AssignLaborCommand(int DwarfId, string LaborId, bool Enabled) : ICommand;

/// <summary>Apply a labor preset (role) to a dwarf, replacing current labors.</summary>
public record ApplyLaborRoleCommand(int DwarfId, string RoleId) : ICommand;

// ── Military ─────────────────────────────────────────────────────────────────

/// <summary>Create a new military squad with a given name.</summary>
public record CreateSquadCommand(string Name) : ICommand;

/// <summary>Disband an existing squad, returning members to civilian roles.</summary>
public record DisbandSquadCommand(int SquadId) : ICommand;

/// <summary>Assign a dwarf to a squad.</summary>
public record AssignDwarfToSquadCommand(int DwarfId, int SquadId) : ICommand;

/// <summary>Toggle a squad's alert status (active deployment).</summary>
public record ToggleSquadAlertCommand(int SquadId, bool Active) : ICommand;

/// <summary>Set the patrol route for a squad.</summary>
public record SetPatrolRouteCommand(int SquadId, Vec3i[] Waypoints) : ICommand;

// ── Save / Load ───────────────────────────────────────────────────────────────

/// <summary>Save the current game to a named slot.</summary>
public record SaveGameCommand(string SlotName) : ICommand;

/// <summary>Load a previously saved game slot.</summary>
public record LoadGameCommand(string SlotName) : ICommand;

// ── World Gen ─────────────────────────────────────────────────────────────────

/// <summary>Generate a new world with the given seed and settings.</summary>
public record GenerateWorldCommand(int Seed, int Width, int Height, int Depth) : ICommand;

/// <summary>
/// Create a fresh embark-ready fortress including world generation, starter units,
/// starter items, stockpiles, and the first workshop.
/// </summary>
public record StartFortressCommand(int Seed, int Width, int Height, int Depth) : ICommand;
