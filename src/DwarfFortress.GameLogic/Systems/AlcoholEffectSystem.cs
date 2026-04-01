using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

public enum IntoxicationState
{
    Sober,
    Tipsy,
    Drunk,
}

public record struct IntoxicationChangedEvent(int EntityId, IntoxicationState OldState, IntoxicationState NewState, float AlcoholLevel);

/// <summary>
/// Converts alcohol concentration in body chemistry into gameplay-facing penalties.
/// The chemistry pipeline produces the raw dose; this system turns that dose into
/// impaired speed, agility, and focus for downstream movement/combat logic.
/// </summary>
public sealed class AlcoholEffectSystem : IGameSystem
{
    public string SystemId    => SystemIds.AlcoholEffectSystem;
    public int    UpdateOrder => 13;
    public bool   IsEnabled   { get; set; } = true;

    private const string SpeedSource   = "alcohol_speed_penalty";
    private const string AgilitySource = "alcohol_agility_penalty";
    private const string FocusSource   = "alcohol_focus_penalty";

    private GameContext? _ctx;

    public void Initialize(GameContext ctx) => _ctx = ctx;

    public void Tick(float delta)
    {
        var registry = _ctx!.Get<EntityRegistry>();

        foreach (var entity in registry.GetAlive<Entity>())
        {
            if (!entity.Components.Has<BodyChemistryComponent>()) continue;
            if (!entity.Components.Has<StatComponent>()) continue;

            var alcohol = entity.Components.Get<BodyChemistryComponent>().Get(SubstanceIds.Alcohol);
            var oldState = GetCurrentState(entity.Components.Get<StatComponent>());
            ApplyState(entity.Components.Get<StatComponent>(), alcohol);
            var newState = Classify(alcohol);

            if (newState != oldState)
                _ctx.EventBus.Emit(new IntoxicationChangedEvent(entity.Id, oldState, newState, alcohol));
        }
    }

    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    private static IntoxicationState Classify(float alcohol)
        => alcohol switch
        {
            >= 0.45f => IntoxicationState.Drunk,
            >= 0.15f => IntoxicationState.Tipsy,
            _        => IntoxicationState.Sober,
        };

    private static IntoxicationState GetCurrentState(StatComponent stats)
    {
        if (stats.Speed.Modifiers.Has(SpeedSource) && stats.Focus.Modifiers.Has(FocusSource))
            return stats.Speed.Modifiers.All.Any(m => m.SourceId == SpeedSource && m.Value <= -0.40f)
                ? IntoxicationState.Drunk
                : IntoxicationState.Tipsy;

        return IntoxicationState.Sober;
    }

    private static void ApplyState(StatComponent stats, float alcohol)
    {
        stats.Speed.Modifiers.Remove(SpeedSource);
        stats.Agility.Modifiers.Remove(AgilitySource);
        stats.Focus.Modifiers.Remove(FocusSource);

        switch (Classify(alcohol))
        {
            case IntoxicationState.Tipsy:
                stats.Speed.Modifiers.Add(new Modifier(SpeedSource, ModType.PercentAdd, -0.15f));
                stats.Agility.Modifiers.Add(new Modifier(AgilitySource, ModType.PercentAdd, -0.10f));
                stats.Focus.Modifiers.Add(new Modifier(FocusSource, ModType.PercentAdd, -0.20f));
                break;

            case IntoxicationState.Drunk:
                stats.Speed.Modifiers.Add(new Modifier(SpeedSource, ModType.PercentAdd, -0.40f));
                stats.Agility.Modifiers.Add(new Modifier(AgilitySource, ModType.PercentAdd, -0.30f));
                stats.Focus.Modifiers.Add(new Modifier(FocusSource, ModType.PercentAdd, -0.50f));
                break;
        }
    }
}