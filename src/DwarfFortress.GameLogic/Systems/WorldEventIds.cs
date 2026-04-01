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

public static class WorldEventDefaults
{
    public const string PrimaryHostileUnitDefId = DefIds.Goblin;
}