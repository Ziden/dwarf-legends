using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Holds a named stat with a base value and a ModifierStack.
/// Resolved value = ModifierStack.Resolve(BaseValue).
/// </summary>
public sealed class Stat
{
    public string         Name      { get; }
    public float          BaseValue { get; set; }
    public ModifierStack  Modifiers { get; } = new();

    public Stat(string name, float baseValue)
    {
        Name      = name;
        BaseValue = baseValue;
    }

    public float Value => Modifiers.Resolve(BaseValue);
}

/// <summary>
/// All base stats for a creature or dwarf.
/// Stats are purely numeric; skills are tracked in SkillComponent.
/// </summary>
public sealed class StatComponent
{
    public Stat Speed     { get; } = new(StatNames.Speed,     1.0f);
    public Stat Strength  { get; } = new(StatNames.Strength,  1.0f);
    public Stat Toughness { get; } = new(StatNames.Toughness, 1.0f);
    public Stat Agility   { get; } = new(StatNames.Agility,   1.0f);
    public Stat Focus     { get; } = new(StatNames.Focus,     1.0f);

    private readonly Dictionary<string, Stat> _all;

    public StatComponent()
    {
        _all = new Dictionary<string, Stat>(StringComparer.OrdinalIgnoreCase)
        {
            [StatNames.Speed]     = Speed,
            [StatNames.Strength]  = Strength,
            [StatNames.Toughness] = Toughness,
            [StatNames.Agility]   = Agility,
            [StatNames.Focus]     = Focus,
        };
    }

    public Stat Get(string statName)
    {
        if (_all.TryGetValue(statName, out var stat))
            return stat;

        throw new KeyNotFoundException($"[StatComponent] Unknown stat: '{statName}'.");
    }

    public bool TryGet(string statName, out Stat stat)
        => _all.TryGetValue(statName, out stat!);

    /// <summary>Tick all modifier stacks forward by delta seconds.</summary>
    public void Tick(float delta)
    {
        foreach (var stat in _all.Values)
            stat.Modifiers.Tick(delta);
    }
}

/// <summary>String constants for stat names. No magic strings in simulation code.</summary>
public static class StatNames
{
    public const string Speed     = "speed";
    public const string Strength  = "strength";
    public const string Toughness = "toughness";
    public const string Agility   = "agility";
    public const string Focus     = "focus";
}
