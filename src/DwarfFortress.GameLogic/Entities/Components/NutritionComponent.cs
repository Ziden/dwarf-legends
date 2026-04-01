using System;

namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks dietary nutrient levels for a dwarf (0–1 each).
/// All stats decay slowly over time and are replenished when eating.
/// A prolonged deficiency in any stat triggers a NutritionDeficiencyEvent.
/// </summary>
public sealed class NutritionComponent
{
    public const float DecayPerSecond    = 0.00025f; // ~67 min to fully deplete
    public const float CriticalThreshold = 0.20f;
    public const float DeficiencySeconds = 60f;      // seconds below threshold before event fires

    public float Carbohydrates { get; private set; } = 1f;
    public float Protein       { get; private set; } = 1f;
    public float Fat           { get; private set; } = 1f;
    public float Vitamins      { get; private set; } = 1f;

    // Seconds spent continuously below the critical threshold (resets on recovery)
    public float CarbsDeficiencySeconds    { get; private set; }
    public float ProteinDeficiencySeconds  { get; private set; }
    public float FatDeficiencySeconds      { get; private set; }
    public float VitaminsDeficiencySeconds { get; private set; }

    public void Decay(float delta)
    {
        Carbohydrates = Math.Max(0f, Carbohydrates - DecayPerSecond * delta);
        Protein       = Math.Max(0f, Protein       - DecayPerSecond * delta);
        Fat           = Math.Max(0f, Fat           - DecayPerSecond * delta);
        Vitamins      = Math.Max(0f, Vitamins      - DecayPerSecond * delta);

        CarbsDeficiencySeconds    = Carbohydrates < CriticalThreshold
            ? CarbsDeficiencySeconds    + delta : 0f;
        ProteinDeficiencySeconds  = Protein       < CriticalThreshold
            ? ProteinDeficiencySeconds  + delta : 0f;
        FatDeficiencySeconds      = Fat           < CriticalThreshold
            ? FatDeficiencySeconds      + delta : 0f;
        VitaminsDeficiencySeconds = Vitamins      < CriticalThreshold
            ? VitaminsDeficiencySeconds + delta : 0f;
    }

    public void Credit(float carbs, float protein, float fat, float vitamins)
    {
        Carbohydrates = Math.Min(1f, Carbohydrates + carbs);
        Protein       = Math.Min(1f, Protein       + protein);
        Fat           = Math.Min(1f, Fat           + fat);
        Vitamins      = Math.Min(1f, Vitamins      + vitamins);
    }

    public bool IsDeficientCarbs    => CarbsDeficiencySeconds    >= DeficiencySeconds;
    public bool IsDeficientProtein  => ProteinDeficiencySeconds  >= DeficiencySeconds;
    public bool IsDeficientFat      => FatDeficiencySeconds      >= DeficiencySeconds;
    public bool IsDeficientVitamins => VitaminsDeficiencySeconds >= DeficiencySeconds;

    public bool AnyDeficiency =>
        IsDeficientCarbs || IsDeficientProtein || IsDeficientFat || IsDeficientVitamins;

    public string? GetWorstDeficiency()
    {
        float worst = 0f;
        string? id = null;
        Check(CarbsDeficiencySeconds,    NutrientIds.Carbohydrates);
        Check(ProteinDeficiencySeconds,  NutrientIds.Protein);
        Check(FatDeficiencySeconds,      NutrientIds.Fat);
        Check(VitaminsDeficiencySeconds, NutrientIds.Vitamins);
        return id;

        void Check(float seconds, string nutrient)
        {
            if (seconds >= DeficiencySeconds && seconds > worst)
            {
                worst = seconds;
                id    = nutrient;
            }
        }
    }
}

public static class NutrientIds
{
    public const string Carbohydrates = "carbohydrates";
    public const string Protein       = "protein";
    public const string Fat           = "fat";
    public const string Vitamins      = "vitamins";
}
