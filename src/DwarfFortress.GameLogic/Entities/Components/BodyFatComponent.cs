namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Tracks body fat for a dwarf. This is separate from the nutrient "Fat" stat.
/// Body fat increases when eating excess calories and decreases when working/hauling.
/// Fat = 0 is normal/healthy. Higher values indicate overweight status.
/// Range: 0-100 (0 = emaciated, 10-20 = healthy, 30+ = overweight, 50+ = obese)
/// </summary>
public sealed class BodyFatComponent
{
    /// <summary>Current body fat percentage. 0 = no fat, 100 = maximum fat.</summary>
    public float BodyFat { get; private set; } = 10f; // Start at healthy baseline

    /// <summary>Rate at which body fat increases when eating (per meal credit).</summary>
    public const float FatGainPerMeal = 0.5f;

    /// <summary>Rate at which body fat decreases when working (per tick of work).</summary>
    public const float FatLossPerWorkSecond = 0.01f;

    /// <summary>Rate at which body fat naturally decays over time (very slow).</summary>
    public const float NaturalDecayPerSecond = 0.0001f;

    /// <summary>Minimum body fat - dwarves always have some fat.</summary>
    public const float MinimumFat = 0f;

    /// <summary>Maximum body fat - dwarves can't get infinitely fat.</summary>
    public const float MaximumFat = 100f;

    /// <summary>
    /// Increases body fat when the dwarf eats a meal.
    /// The amount is reduced if the dwarf has the Fit trait.
    /// </summary>
    public void GainFat(float amount)
    {
        BodyFat = System.Math.Min(MaximumFat, BodyFat + amount);
    }

    /// <summary>
    /// Decreases body fat when the dwarf works or hauls items.
    /// </summary>
    public void LoseFat(float amount)
    {
        BodyFat = System.Math.Max(MinimumFat, BodyFat - amount);
    }

    /// <summary>
    /// Natural decay of body fat over time (very slow).
    /// </summary>
    public void Decay(float delta)
    {
        BodyFat = System.Math.Max(MinimumFat, BodyFat - NaturalDecayPerSecond * delta);
    }

    /// <summary>
    /// Returns the body fat contribution to weight in kg.
    /// Formula: bodyFat * heightFactor (taller dwarves weigh more for same fat %)
    /// </summary>
    public float GetFatWeight(float heightCm)
    {
        // Base weight from fat: each % of fat adds ~0.5kg per cm of height above 100cm
        var heightFactor = System.Math.Max(0f, (heightCm - 100f) / 60f); // 0-1 scale
        return BodyFat * 0.5f * (1f + heightFactor);
    }
}