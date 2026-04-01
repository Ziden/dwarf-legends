namespace DwarfFortress.GameLogic.Entities;

/// <summary>
/// Base class for all runtime entities: dwarves, creatures, items, buildings.
/// Carries a unique integer ID, a definition ID, and a component bag.
/// </summary>
public abstract class Entity
{
    /// <summary>Globally unique, auto-incremented integer ID. Used in save data and events.</summary>
    public int Id { get; }

    /// <summary>References the immutable definition in a Registry (e.g. "dwarf", "iron_bar").</summary>
    public string DefId { get; }

    /// <summary>False once the entity has been killed/destroyed. Dead entities are retained in the
    /// registry until garbage collected so event handlers can still inspect them.</summary>
    public bool IsAlive { get; private set; } = true;

    public ComponentBag Components { get; } = new();

    protected Entity(int id, string defId)
    {
        Id    = id;
        DefId = defId ?? throw new System.ArgumentNullException(nameof(defId));
    }

    /// <summary>Mark this entity as dead. Cannot be reversed.</summary>
    public void Kill() => IsAlive = false;
}
