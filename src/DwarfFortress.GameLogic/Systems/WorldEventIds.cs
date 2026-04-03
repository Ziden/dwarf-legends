using DwarfFortress.GameLogic.Entities;

namespace DwarfFortress.GameLogic.Systems;

public static class WorldEventIds
{
    public const string MigrantWave = "migrant_wave";
    public const string GoblinRaid = "goblin_raid";
}

public static class WorldEventTriggerTypes
{
    public const string SeasonChange = "season_change";
    public const string DayStart = "day_start";
}

public static class WorldEventEffectOps
{
    public const string SpawnMigrants = "spawn_migrants";
    public const string SpawnHostiles = "spawn_hostiles";
}

public static class WorldEventTargetTypes
{
    public const string AllDwarves = "all_dwarves";
    public const string DwarvesWithProfession = "dwarves_with_profession";
    public const string DwarvesWithAttribute = "dwarves_with_attribute";
    public const string DwarvesWithLabor = "dwarves_with_labor";
    public const string AllCreatures = "all_creatures";
    public const string HostileCreatures = "hostile_creatures";
    public const string EntitiesWithFactionRole = "entities_with_faction_role";
    public const string EntitiesWithDef = "entities_with_def";
}

public static class WorldEventDefaults
{
    public const string PrimaryHostileUnitDefId = "";
}
