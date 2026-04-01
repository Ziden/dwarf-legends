using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class DiscoverySystemTests
{
    private const string SmelterBuildingId = "smelter";

    [Fact]
    public void DiscoverySystem_Does_Not_Unlock_MultiItem_Building_Until_Quantity_Is_Met()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();
        var dwarf = new Dwarf(er.NextId(), "Hauler", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        var firstStone = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(1, 1, 0));
        items.PickUpItem(firstStone.Id, dwarf.Id, dwarf.Position.Position);

        Assert.False(discovery.IsBuildingUnlocked(SmelterBuildingId));

        var secondStone = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(2, 1, 0));
        items.PickUpItem(secondStone.Id, dwarf.Id, dwarf.Position.Position);

        Assert.False(discovery.IsBuildingUnlocked(SmelterBuildingId));

        var thirdStone = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(3, 1, 0));
        items.PickUpItem(thirdStone.Id, dwarf.Id, dwarf.Position.Position);

        Assert.True(discovery.IsBuildingUnlocked(SmelterBuildingId));
        Assert.Equal(ItemDefIds.GraniteBoulder, discovery.GetDiscoveredBy(SmelterBuildingId));
    }
}