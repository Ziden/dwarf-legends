using System;
using System.Collections.Generic;

namespace DwarfFortress.GameLogic.Entities.Components;

public static class SkillIds
{
    public const string Mining = LaborIds.Mining;
    public const string WoodCutting = LaborIds.WoodCutting;
    public const string Construction = LaborIds.Construction;
    public const string Crafting = LaborIds.Crafting;
}

/// <summary>A single skill with its current level and XP progress.</summary>
public sealed class Skill
{
    public string Name  { get; }
    public int    Level { get; private set; }
    public float  Xp    { get; private set; }

    /// <summary>XP required to go from level N to N+1.</summary>
    public float XpForNextLevel => 100f + Level * 50f;

    public Skill(string name, int startLevel = 0)
    {
        Name  = name;
        Level = startLevel;
    }

    public void AddXp(float amount)
    {
        Xp += amount;
        while (Xp >= XpForNextLevel)
        {
            Xp    -= XpForNextLevel;
            Level += 1;
        }
    }

    /// <summary>Directly restores level and XP — for save/load only.</summary>
    public void RestoreState(int level, float xp)
    {
        Level = level;
        Xp    = xp;
    }
}

/// <summary>All skill levels for an entity. Skills are created lazily on first access.</summary>
public sealed class SkillComponent
{
    private readonly Dictionary<string, Skill> _skills = new(StringComparer.OrdinalIgnoreCase);

    public Skill GetOrCreate(string skillId)
    {
        if (!_skills.TryGetValue(skillId, out var skill))
        {
            skill = new Skill(skillId);
            _skills[skillId] = skill;
        }
        return skill;
    }

    public int GetLevel(string skillId)
        => _skills.TryGetValue(skillId, out var s) ? s.Level : 0;

    public void AddXp(string skillId, float amount)
        => GetOrCreate(skillId).AddXp(amount);

    /// <summary>Directly restores skill state — for save/load only.</summary>
    public void RestoreSkill(string skillId, int level, float xp)
        => GetOrCreate(skillId).RestoreState(level, xp);

    public IReadOnlyDictionary<string, Skill> All => _skills;
}
