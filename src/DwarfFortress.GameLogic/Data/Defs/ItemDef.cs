using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>
/// Optional nutrition profile for food items. When specified, overrides tag-based nutrition inference.
/// </summary>
public sealed record NutritionProfile(
    float Carbs    = 0.4f,
    float Protein  = 0.4f,
    float Fat      = 0.3f,
    float Vitamins = 0.4f);

/// <summary>
/// Immutable definition of an item type (log, plank, ore, bar, bed, etc.).
/// Loaded from items.json.
/// </summary>
public sealed record ItemDef(
    string                Id,
    string                DisplayName,
    TagSet                Tags,
    bool                  Stackable     = false,
    int                   MaxStack      = 1,
    float                 Weight        = 1.0f,
    int                   BaseValue     = 1,
    IReadOnlyList<EffectBlock>? UseEffects = null,  // effects when consumed/used
    NutritionProfile?     Nutrition    = null);       // explicit nutrition values (overrides tag inference)
