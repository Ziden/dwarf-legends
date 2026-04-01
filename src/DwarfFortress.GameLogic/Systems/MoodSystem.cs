using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct MoodChangedEvent(int DwarfId, Mood OldMood, Mood NewMood);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Recalculates dwarf mood each tick from accumulated thoughts.
/// Emits MoodChangedEvent when mood crosses a threshold.
/// Order 7.
/// </summary>
public sealed class MoodSystem : IGameSystem
{
    public string SystemId    => SystemIds.MoodSystem;
    public int    UpdateOrder => 7;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        foreach (var dwarf in _ctx!.Get<EntityRegistry>().GetAlive<Dwarf>())
        {
            var mood     = dwarf.Components.Get<MoodComponent>();
            var thoughts = dwarf.Components.Get<ThoughtComponent>();

            var newHappiness = System.Math.Clamp(thoughts.TotalHappiness, -1f, 1f);
            var newMood      = MoodComponent.FromHappiness(newHappiness);

            if (newMood != mood.Current)
            {
                var old = mood.Current;
                mood.Happiness = newHappiness;
                mood.Current   = newMood;
                _ctx.EventBus.Emit(new MoodChangedEvent(dwarf.Id, old, newMood));
            }
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }
}
