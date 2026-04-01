using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class CorpseIntegrationTests
{
    [Fact]
    public void StockpileManager_Prefers_Explicit_Refuse_Stockpile_For_Corpses()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var hauler = new Dwarf(er.NextId(), "Refuse Hauler", new Vec3i(5, 5, 0));
        hauler.Labors.DisableAll();
        hauler.Labors.Enable(LaborIds.Hauling);
        er.Register(hauler);

        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(10, 10, 0),
            To: new Vec3i(10, 10, 0),
            AcceptedTags: []));
        sim.Context.Commands.Dispatch(new CreateStockpileCommand(
            From: new Vec3i(12, 10, 0),
            To: new Vec3i(12, 10, 0),
            AcceptedTags: [TagIds.Refuse]));

        var refuseStockpile = sim.Context.Get<StockpileManager>().GetAll()
            .Single(stockpile => stockpile.AcceptedTags.Contains(TagIds.Refuse));

        var corpse = items.CreateItem(ItemDefIds.Corpse, string.Empty, new Vec3i(6, 5, 0));
        corpse.Components.Add(new CorpseComponent(99, DefIds.Elk, "Elk", "test"));

        for (var tick = 0; tick < 400; tick++)
            sim.Tick(0.1f);

        Assert.Equal(refuseStockpile.Id, corpse.StockpileId);
        Assert.Equal(new Vec3i(12, 10, 0), corpse.Position.Position);
    }

    [Fact]
    public void ThoughtSystem_Adds_HandledCorpse_Thought_When_Dwarf_Picks_Up_Corpse()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(4, 4, 0));
        er.Register(dwarf);

        var corpse = items.CreateItem(ItemDefIds.Corpse, string.Empty, dwarf.Position.Position);
        corpse.Components.Add(new CorpseComponent(42, DefIds.Elk, "Elk", "test"));

        items.PickUpItem(corpse.Id, dwarf.Id, dwarf.Position.Position);

        Assert.Contains(dwarf.Thoughts.Active, thought => thought.Id == ThoughtIds.HandledCorpse);
    }

    [Fact]
    public void ThoughtSystem_Adds_RottingCorpse_Thought_When_Dwarf_Stands_Near_One()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Witness", new Vec3i(8, 8, 0));
        er.Register(dwarf);

        var corpse = items.CreateItem(ItemDefIds.Corpse, string.Empty, dwarf.Position.Position + Vec3i.East);
        corpse.Components.Add(new CorpseComponent(77, DefIds.Elk, "Elk", "test"));
        var rot = new RotComponent();
        rot.Restore(0.6f);
        corpse.Components.Add(rot);

        sim.Tick(0.1f);

        Assert.Contains(dwarf.Thoughts.Active, thought => thought.Id == ThoughtIds.NearbyRottingCorpse);
    }
}