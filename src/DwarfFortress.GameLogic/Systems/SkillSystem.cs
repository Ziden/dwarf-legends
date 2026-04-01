using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Jobs;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct SkillLeveledUpEvent(int DwarfId, string SkillId, int NewLevel);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Awards skill XP when jobs are completed and handles level-up events.
/// Order 8.
/// </summary>
public sealed class SkillSystem : IGameSystem
{
    public string SystemId    => SystemIds.SkillSystem;
    public int    UpdateOrder => 8;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.EventBus.On<JobCompletedEvent>(OnJobCompleted);
    }

    public void Tick(float delta) { /* XP applied on job completion events */ }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Private ────────────────────────────────────────────────────────────

    private void OnJobCompleted(JobCompletedEvent e)
    {
        var registry = _ctx!.Get<EntityRegistry>();
        if (!registry.TryGetById<Dwarf>(e.DwarfId, out var dwarf) || dwarf is null) return;

        var skillId = JobDefIdToSkillId(e.JobDefId);
        if (skillId is null) return;

        var skills    = dwarf.Components.Get<SkillComponent>();
        var oldLevel  = skills.GetOrCreate(skillId).Level;
        skills.AddXp(skillId, 25f);
        var newLevel  = skills.GetOrCreate(skillId).Level;

        if (newLevel > oldLevel)
            _ctx.EventBus.Emit(new SkillLeveledUpEvent(e.DwarfId, skillId, newLevel));
    }

    private static string? JobDefIdToSkillId(string jobDefId) => jobDefId switch
    {
        JobDefIds.MineTile  => SkillIds.Mining,
        JobDefIds.CutTree   => SkillIds.WoodCutting,
        JobDefIds.Construct => SkillIds.Construction,
        JobDefIds.Craft     => SkillIds.Crafting,
        _                   => null,
    };
}
