namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Marks an item as a corpse and records who or what it used to be.
/// </summary>
public sealed class CorpseComponent
{
    public int FormerEntityId { get; }
    public string FormerDefId { get; }
    public string DisplayName { get; }
    public string DeathCause { get; }

    public CorpseComponent(int formerEntityId, string formerDefId, string displayName, string deathCause)
    {
        FormerEntityId = formerEntityId;
        FormerDefId = formerDefId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? formerDefId : displayName;
        DeathCause = string.IsNullOrWhiteSpace(deathCause) ? "unknown" : deathCause;
    }
}