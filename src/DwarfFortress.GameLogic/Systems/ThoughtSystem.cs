using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct ThoughtAddedEvent(int DwarfId, string ThoughtId, float HappinessMod);

public static class ThoughtIds
{
    public const string JobDone = "thought_job_done";
    public const string AteFineMeal = "thought_ate_fine_meal";
    public const string AteLavishMeal = "thought_ate_lavish_meal";
    public const string ComradeDied = "thought_comrade_died";
    public const string HandledCorpse = "thought_handled_corpse";
    public const string NearbyCorpse = "thought_nearby_corpse";
    public const string NearbyRottingCorpse = "thought_nearby_rotting_corpse";
    public const string Hungry = "thought_hungry";
    public const string Thirsty = "thought_thirsty";
    public const string SleepDeprived = "thought_sleep_deprived";
    public const string Socialized = "thought_socialized";
    public const string Tantrum = "thought_tantrum";
    public const string SkillLeveledUp = "thought_skill_leveled";
    public const string NutritionalDeficiency = "thought_nutritional_deficiency";
    public const string AteLikedFood = "thought_ate_liked_food";
    public const string AteDislikedFood = "thought_ate_disliked_food";
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Ticks thoughts on all dwarves. Subscribes to game events to add thoughts.
/// Order 6.
/// </summary>
public sealed class ThoughtSystem : IGameSystem
{
    public string SystemId    => SystemIds.ThoughtSystem;
    public int    UpdateOrder => 6;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    // Thought definitions: id → (description, happinessMod, duration in seconds)
    private static readonly (string Id, string Desc, float Happiness, float Duration)[] Thoughts =
    [
        (ThoughtIds.JobDone,        "Satisfied with work",          0.05f,  3600f),
        (ThoughtIds.AteFineMeal,    "Eaten a fine meal",            0.10f,  3600f),
        (ThoughtIds.AteLavishMeal,  "Eaten a lavish meal",          0.20f,  7200f),
        (ThoughtIds.ComradeDied,    "A fellow dwarf has died",     -0.30f,  7200f),
        (ThoughtIds.HandledCorpse,  "Handled a corpse",            -0.08f,  2400f),
        (ThoughtIds.NearbyCorpse,   "Saw a corpse nearby",         -0.06f,   300f),
        (ThoughtIds.NearbyRottingCorpse, "Stood near a rotting corpse", -0.18f, 300f),
        (ThoughtIds.Hungry,         "Has been very hungry",        -0.10f,  1800f),
        (ThoughtIds.Thirsty,        "Has been very thirsty",       -0.15f,  1800f),
        (ThoughtIds.SleepDeprived,  "Slept on the ground",         -0.05f,  3600f),
        (ThoughtIds.SkillLeveledUp,  "Proud of skill improvement",   0.12f,  3600f),
        (ThoughtIds.NutritionalDeficiency, "Suffering from poor diet", -0.10f, 1800f),
        (ThoughtIds.AteLikedFood,   "Enjoyed a favourite food",     0.15f,  3600f),
        (ThoughtIds.AteDislikedFood, "Forced down a hated food",   -0.12f,  3600f),
    ];

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;

        ctx.EventBus.On<JobCompletedEvent>(OnJobCompleted);
        ctx.EventBus.On<Jobs.JobFailedEvent>(OnJobFailed);
        ctx.EventBus.On<NeedCriticalEvent>(OnNeedCritical);
        ctx.EventBus.On<Entities.EntityKilledEvent>(OnEntityKilled);
        ctx.EventBus.On<ItemPickedUpEvent>(OnItemPickedUp);
        ctx.EventBus.On<SkillLeveledUpEvent>(OnSkillLeveledUp);
        ctx.EventBus.On<NutritionDeficiencyEvent>(OnNutritionDeficiency);
        ctx.EventBus.On<MealPreferenceEvent>(e => AddThought(e.DwarfId, e.ThoughtId));
    }

    public void Tick(float delta)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        foreach (var dwarf in _ctx.Get<EntityRegistry>().GetAlive<Dwarf>())
        {
            dwarf.Components.Get<ThoughtComponent>().Tick(delta);
            UpdateCorpseExposureThoughts(dwarf, itemSystem);
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnJobCompleted(JobCompletedEvent e)
    {
        AddThought(e.DwarfId, ThoughtIds.JobDone);
    }

    private void OnJobFailed(Jobs.JobFailedEvent e)
    {
        // No thought on fail; handled by NeedsSystem
    }

    private void OnNeedCritical(NeedCriticalEvent e)
    {
        var thoughtId = e.NeedId switch
        {
            NeedIds.Hunger => ThoughtIds.Hungry,
            NeedIds.Thirst => ThoughtIds.Thirsty,
            NeedIds.Sleep  => ThoughtIds.SleepDeprived,
            _              => null,
        };
        if (thoughtId is not null) AddThought(e.EntityId, thoughtId);
    }

    private void OnEntityKilled(Entities.EntityKilledEvent e)
    {
        if (!_ctx!.Get<EntityRegistry>().TryGetById<Dwarf>(e.EntityId, out _)) return;

        // A dwarf died — all surviving dwarves get the negative thought
        foreach (var survivor in _ctx.Get<EntityRegistry>().GetAlive<Dwarf>())
            if (survivor.Id != e.EntityId)
                AddThought(survivor.Id, ThoughtIds.ComradeDied);
    }

    private void OnSkillLeveledUp(SkillLeveledUpEvent e)
    {
        AddThought(e.DwarfId, ThoughtIds.SkillLeveledUp);
    }

    private void OnNutritionDeficiency(NutritionDeficiencyEvent e)
    {
        AddThought(e.DwarfId, ThoughtIds.NutritionalDeficiency);
    }

    private void OnItemPickedUp(ItemPickedUpEvent e)
    {
        var itemSystem = _ctx!.TryGet<ItemSystem>();
        if (itemSystem is null || !itemSystem.TryGetItem(e.ItemId, out var item) || item is null)
            return;
        if (item.Components.TryGet<CorpseComponent>() is null)
            return;
        AddThought(e.CarrierEntityId, ThoughtIds.HandledCorpse);
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void AddThought(int dwarfId, string thoughtId)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Dwarf>(dwarfId, out var dwarf) || dwarf is null) return;

        var def = System.Array.Find(Thoughts, t => t.Id == thoughtId);
        if (def.Id is null) return;

        var thoughts = dwarf.Components.Get<ThoughtComponent>();
        var alreadyHadThought = thoughts.HasThought(thoughtId);
        thoughts.AddThought(new Thought(def.Id, def.Desc, def.Happiness, def.Duration));

        if (!alreadyHadThought)
            _ctx.EventBus.Emit(new ThoughtAddedEvent(dwarfId, thoughtId, def.Happiness));
    }

    private void UpdateCorpseExposureThoughts(Dwarf dwarf, ItemSystem? itemSystem)
    {
        if (itemSystem is null)
            return;

        var thoughts = dwarf.Components.Get<ThoughtComponent>();
        var nearbyCorpse = false;
        var nearbyRottingCorpse = false;

        foreach (var pos in EnumerateNearbyPositions(dwarf.Position.Position))
        {
            foreach (var item in itemSystem.GetItemsAt(pos))
            {
                if (item.Components.TryGet<CorpseComponent>() is null)
                    continue;

                nearbyCorpse = true;
                if ((item.Components.TryGet<RotComponent>()?.Progress ?? 0f) >= 0.45f)
                    nearbyRottingCorpse = true;
            }
        }

        if (nearbyRottingCorpse)
            AddThought(dwarf.Id, ThoughtIds.NearbyRottingCorpse);
        else if (nearbyCorpse)
            AddThought(dwarf.Id, ThoughtIds.NearbyCorpse);
    }

    private static System.Collections.Generic.IEnumerable<Vec3i> EnumerateNearbyPositions(Vec3i center)
    {
        yield return center;
        yield return center + Vec3i.North;
        yield return center + Vec3i.South;
        yield return center + Vec3i.East;
        yield return center + Vec3i.West;
    }
}
