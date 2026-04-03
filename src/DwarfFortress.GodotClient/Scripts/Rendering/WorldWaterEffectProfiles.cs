using System.Collections.Generic;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GodotClient.Bootstrap;
using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.GodotClient.Rendering;

public static class WorldWaterEffectProfiles
{
    public static readonly RenderCache.WaterEffectStyle DwarfStyle = new(
        RippleScale: 1.00f, BubbleScale: 1.00f, WakeScale: 1.00f, SubmergeScale: 1.00f,
        MotionThreshold: 0.35f, SuppressBubbles: false, WakePattern: RenderCache.WaterWakePattern.Default);

    private static readonly RenderCache.WaterEffectStyle CreatureStyle = new(
        RippleScale: 0.95f, BubbleScale: 0.90f, WakeScale: 0.95f, SubmergeScale: 0.94f,
        MotionThreshold: 0.35f, SuppressBubbles: false, WakePattern: RenderCache.WaterWakePattern.Default);

    private static readonly RenderCache.WaterEffectStyle LargeCreatureStyle = new(
        RippleScale: 1.22f, BubbleScale: 1.20f, WakeScale: 1.26f, SubmergeScale: 1.08f,
        MotionThreshold: 0.24f, SuppressBubbles: false, WakePattern: RenderCache.WaterWakePattern.Default);

    private static readonly RenderCache.WaterEffectStyle PetCreatureStyle = new(
        RippleScale: 0.86f, BubbleScale: 0.74f, WakeScale: 0.82f, SubmergeScale: 0.86f,
        MotionThreshold: 0.32f, SuppressBubbles: false, WakePattern: RenderCache.WaterWakePattern.Default);

    private static readonly RenderCache.WaterEffectStyle AquaticCreatureStyle = new(
        RippleScale: 0.74f, BubbleScale: 0.36f, WakeScale: 1.06f, SubmergeScale: 0.78f,
        MotionThreshold: 0.12f, SuppressBubbles: true, WakePattern: RenderCache.WaterWakePattern.SwimV);

    public static RenderCache.WaterEffectStyle ResolveCreatureStyle(
        Creature creature,
        DataManager? data,
        Dictionary<string, RenderCache.WaterEffectStyle> cache)
    {
        if (cache.TryGetValue(creature.DefId, out var cached))
            return cached;

        var def = data?.Creatures.GetOrNull(creature.DefId);
        var authoredStyleId =
            data?.ContentQueries?.ResolveCreatureVisuals(creature.DefId)?.WaterEffectStyleId ??
            ClientContentQueries.ResolveCreatureWaterEffectStyleId(creature.DefId);

        var resolved = authoredStyleId switch
        {
            ContentCreatureWaterEffectStyleIds.Aquatic => AquaticCreatureStyle,
            ContentCreatureWaterEffectStyleIds.Large => LargeCreatureStyle,
            ContentCreatureWaterEffectStyleIds.Pet => PetCreatureStyle,
            ContentCreatureWaterEffectStyleIds.Default => CreatureStyle,
            _ => def?.IsAquatic() == true
                ? AquaticCreatureStyle
                : def?.Tags.Contains(TagIds.Large) == true
                    ? LargeCreatureStyle
                    : def?.Tags.Contains(TagIds.Pet) == true
                        ? PetCreatureStyle
                        : CreatureStyle,
        };

        cache[creature.DefId] = resolved;
        return resolved;
    }
}
