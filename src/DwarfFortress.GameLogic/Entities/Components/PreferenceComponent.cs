namespace DwarfFortress.GameLogic.Entities.Components;

/// <summary>
/// Records a dwarf's food like and dislike.
/// Each slot holds a food item def ID and a strength byte (0 = extreme dislike, 255 = extreme like).
/// For now dwarves have at most one liked food and one disliked food.
/// </summary>
public sealed class PreferenceComponent
{
    /// <summary>Item def ID of the food this dwarf likes, or null if none set.</summary>
    public string? LikedFoodId { get; set; }

    /// <summary>
    /// How strongly the dwarf likes it (0-255, where 255 is maximum love).
    /// Only meaningful when <see cref="LikedFoodId"/> is non-null.
    /// </summary>
    public byte LikeStrength { get; set; }

    /// <summary>Item def ID of the food this dwarf dislikes, or null if none set.</summary>
    public string? DislikedFoodId { get; set; }

    /// <summary>
    /// How strongly the dwarf dislikes it (0-255, where 0 is maximum hate).
    /// Only meaningful when <see cref="DislikedFoodId"/> is non-null.
    /// </summary>
    public byte DislikeStrength { get; set; }

    /// <summary>Returns the happiness modifier for eating the given food item, or 0 if no preference.</summary>
    public float GetFoodMoodMod(string itemDefId)
    {
        if (LikedFoodId is not null &&
            string.Equals(LikedFoodId, itemDefId, System.StringComparison.OrdinalIgnoreCase))
            return +0.15f;

        if (DislikedFoodId is not null &&
            string.Equals(DislikedFoodId, itemDefId, System.StringComparison.OrdinalIgnoreCase))
            return -0.12f;

        return 0f;
    }
}
