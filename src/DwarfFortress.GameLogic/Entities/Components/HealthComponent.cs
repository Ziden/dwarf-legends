using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>Severity of a wound.</summary>
public enum WoundSeverity { Minor, Serious, Critical }

/// <summary>A wound on a specific body part.</summary>
public sealed class Wound
{
    public string        BodyPartId { get; }
    public WoundSeverity Severity   { get; }
    public bool          IsBleeding { get; set; }
    public float         BleedRate  { get; }      // health per second lost while bleeding

    public Wound(string bodyPartId, WoundSeverity severity, bool isBleeding = false)
    {
        BodyPartId = bodyPartId;
        Severity   = severity;
        IsBleeding = isBleeding;
        BleedRate  = severity switch
        {
            WoundSeverity.Minor    => 0.01f,
            WoundSeverity.Serious  => 0.05f,
            WoundSeverity.Critical => 0.15f,
            _                      => 0.01f,
        };
    }
}

/// <summary>Combat/health state for an entity.</summary>
public sealed class HealthComponent
{
    public float MaxHealth     { get; }
    public float CurrentHealth { get; private set; }
    public bool  IsConscious   { get; private set; } = true;

    // Consciousness is lost below this fraction of max health and restored above it.
    private const float ConsciousnessThreshold = 0.25f;

    private readonly List<Wound> _wounds = new();

    public HealthComponent(float maxHealth)
    {
        MaxHealth     = maxHealth;
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        CurrentHealth -= amount;
        if (CurrentHealth <= MaxHealth * ConsciousnessThreshold) IsConscious = false;
        if (CurrentHealth < 0) CurrentHealth = 0;
    }

    public void Heal(float amount)
    {
        CurrentHealth = System.Math.Min(MaxHealth, CurrentHealth + amount);
        if (CurrentHealth > MaxHealth * ConsciousnessThreshold) IsConscious = true;
    }

    public void AddWound(Wound wound) => _wounds.Add(wound);

    public void Tick(float delta)
    {
        float bleed = 0;
        foreach (var w in _wounds)
            if (w.IsBleeding) bleed += w.BleedRate;

        if (bleed > 0) TakeDamage(bleed * delta);
    }

    public IReadOnlyList<Wound> Wounds => _wounds;
    public bool IsDead => CurrentHealth <= 0f;
    public float HealthPercent => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0;

    /// <summary>Directly restores health state — for save/load only.</summary>
    public void Restore(float currentHealth, bool isConscious)
    {
        CurrentHealth = System.Math.Clamp(currentHealth, 0, MaxHealth);
        IsConscious   = isConscious;
    }
}
