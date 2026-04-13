using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GodotClient.UI;


public static class UiText
{
    public const string Yes = "Yes";
    public const string No = "No";
    public const string Idle = "Idle";
    public const string Permanent = "permanent";
    public const string Overview = "Overview";
    public const string Thoughts = "Thoughts";
    public const string Vitals = "Vitals";
    public const string Needs = "Needs";
    public const string Stats = "Stats";
    public const string CurrentJob = "Current Job";
    public const string Wounds = "Wounds";
    public const string BodyChemistry = "Body Chemistry";
    public const string Conscious = "Conscious";
    public const string Unconscious = "Unconscious";
    public const string Hostile = "Hostile";
    public const string Neutral = "Neutral";
    public const string HostileLower = "hostile";
    public const string NeutralLower = "neutral";
    public const string HostileCreature = "Hostile creature";
    public const string NeutralCreature = "Neutral creature";
    public const string ChooseBuilding = "Choose building:";
    public const string NoBuildingDefinitionsLoaded = "No building definitions loaded.";
    public const string NoActiveJob = "No active job. The dwarf is idle or waiting for assignment.";
    public const string CreatureNoActiveJob = "No active job. This creature follows autonomous behavior.";
    public const string NoCarriedItems = "No carried items.";
    public const string NoActiveThoughts = "No active thoughts.";
    public const string NoTrainedSkills = "No trained skills yet.";
    public const string NoActiveWounds = "No active wounds.";
    public const string NoTrackedSubstances = "No tracked substances.";
    public const string NoTrackedNeeds = "No tracked needs.";
    public const string HostileCreatureThoughtsUnavailable = "Hostile creatures do not expose detailed thought logs yet.";
    public const string CreatureThoughtsUnavailable = "Creatures do not expose detailed thought logs yet.";
    public const string HostileCreatureLaborsUnavailable = "Labor assignment is not available for hostile creatures.";
    public const string CreatureLaborsUnavailable = "Labor assignment is only available for fortress dwarves.";
    public const string EventLog = "Event Log";
    public const string NoRecentEvents = "No recent events recorded for this creature yet.";
    public const string WorldEventPrefix = "World event:";

    public static string YesNo(bool value) => value ? Yes : No;

    public static string ConsciousState(bool isConscious)
        => isConscious ? Conscious : Unconscious;

    public static string CreatureDisposition(bool isHostile)
        => isHostile ? Hostile : Neutral;

    public static string CreatureSummaryDisposition(bool isHostile)
        => isHostile ? HostileCreature : NeutralCreature;

    public static string CreatureAttitude(bool isHostile)
        => isHostile ? HostileLower : NeutralLower;

    public static string ModeLabel(InputMode mode) => mode switch
    {
        InputMode.Select => "Select",
        InputMode.DesignateClear => "HARVEST",
        InputMode.DesignateMine => "MINING",
        InputMode.DesignateCutTrees => "CHOPPING",
        InputMode.DesignateCancel => "CANCEL DESIGNATION",
        InputMode.StockpileZone => "STOCKPILE ZONE",
        InputMode.BuildingPreview => "PLACE BUILDING",
        _ => mode.ToString(),
    };

    public static string ModeHint(InputMode mode) => mode switch
    {
        InputMode.Select => "Click to select a dwarf, creature, or building",
        InputMode.DesignateClear => "Drag over walls and trees to queue harvesting",
        InputMode.DesignateMine => "Drag over stone/soil walls to queue mining",
        InputMode.DesignateCutTrees => "Drag over trees to queue chopping",
        InputMode.DesignateCancel => "Drag over designated tiles to cancel",
        InputMode.StockpileZone => "Drag to define a stockpile zone",
        InputMode.BuildingPreview => "Click to place  |  R to rotate  |  Right-click to cancel",
        _ => string.Empty,
    };
}
