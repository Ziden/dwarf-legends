using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

/// <summary>
/// End-to-end checks for the contamination -> grooming -> alcohol impairment chain.
/// </summary>
public sealed class EmergentAlcoholTests
{
    [Fact]
    public void Cat_Grooms_Beer_Coating_And_Gains_Alcohol()
    {
        var (sim, map, er, _, _) = TestFixtures.BuildFullSim();

        var cat = new Creature(er.NextId(), DefIds.Cat, new Vec3i(5, 5, 0), maxHealth: 20f);
        er.Register(cat);

        var tile = map.GetTile(cat.Position.Position);
        tile.CoatingMaterialId = "beer";
        tile.CoatingAmount     = 1.0f;
        map.SetTile(cat.Position.Position, tile);

        for (int i = 0; i < 5; i++) sim.Tick(0.1f);

        Assert.True(cat.BodyChemistry.Get(SubstanceIds.Alcohol) > 0f,
            "Expected grooming to ingest beer coating as alcohol.");
    }

    [Fact]
    public void AlcoholEffectSystem_Applies_And_Removes_Stat_Penalties()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var cat = new Creature(er.NextId(), DefIds.Cat, new Vec3i(1, 1, 0), maxHealth: 20f);
        er.Register(cat);

        cat.BodyChemistry.AddSubstance(SubstanceIds.Alcohol, 0.50f);
        sim.Tick(0.1f);

        Assert.True(cat.Stats.Speed.Value < cat.Stats.Speed.BaseValue);
        Assert.True(cat.Stats.Agility.Value < cat.Stats.Agility.BaseValue);
        Assert.True(cat.Stats.Focus.Value < cat.Stats.Focus.BaseValue);

        for (int i = 0; i < 600; i++) sim.Tick(0.1f);

        Assert.Equal(cat.Stats.Speed.BaseValue, cat.Stats.Speed.Value);
        Assert.Equal(cat.Stats.Agility.BaseValue, cat.Stats.Agility.Value);
        Assert.Equal(cat.Stats.Focus.BaseValue, cat.Stats.Focus.Value);
    }
}