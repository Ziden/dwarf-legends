using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;

namespace DwarfFortress.GameLogic.Data.Defs;

/// <summary>A required input for a recipe: item tags + quantity.</summary>
public sealed record RecipeInput(TagSet RequiredTags, int Quantity);

/// <summary>An output produced by completing a recipe: item def ID + quantity.</summary>
public sealed record RecipeOutput(string ItemDefId, int Quantity, string? MaterialInheritFrom = null);

/// <summary>
/// Immutable definition of a crafting recipe.
/// Inputs are tag-based so any matching material works.
/// Loaded from recipes.json.
/// </summary>
public sealed record RecipeDef(
    string                      Id,
    string                      DisplayName,
    string                      WorkshopDefId,
    string                      RequiredLaborId,
    IReadOnlyList<RecipeInput>  Inputs,
    IReadOnlyList<RecipeOutput> Outputs,
    float                       WorkTime = 100f,   // ticks to complete
    int                         SkillXp  = 10);
