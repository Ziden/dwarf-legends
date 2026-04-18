using System.Collections.Generic;
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
    public void DiscoverySystem_Separates_Discovery_From_Current_Buildability()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();

        var firstBoulder = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(1, 1, 0));

        Assert.True(discovery.IsBuildingUnlocked(SmelterBuildingId));
        Assert.Equal(DiscoveryKnowledgeState.Unlocked, discovery.GetBuildingState(SmelterBuildingId));
        Assert.Equal(ItemDefIds.GraniteBoulder, discovery.GetDiscoveredBy(SmelterBuildingId));

        var secondBoulder = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(2, 1, 0));

        Assert.Equal(DiscoveryKnowledgeState.Unlocked, discovery.GetBuildingState(SmelterBuildingId));

        var thirdBoulder = items.CreateItem(ItemDefIds.GraniteBoulder, "granite", new Vec3i(3, 1, 0));
        var box = new Box(er.NextId(), new Vec3i(6, 6, 0));
        er.Register(box);
        items.StoreItemInBox(firstBoulder.Id, box);
        items.StoreItemInBox(secondBoulder.Id, box);
        items.StoreItemInBox(thirdBoulder.Id, box);

        Assert.Equal(DiscoveryKnowledgeState.BuildableNow, discovery.GetBuildingState(SmelterBuildingId));
    }

    [Fact]
    public void DiscoverySystem_Resolves_Derived_Craftable_Outputs()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();
        var dwarf = new Dwarf(er.NextId(), "Smelter", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        var oreA = items.CreateItem(ItemDefIds.IronOre, "iron", new Vec3i(1, 1, 0));
        items.PickUpItem(oreA.Id, dwarf.Id, dwarf.Position.Position);
        var oreB = items.CreateItem(ItemDefIds.IronOre, "iron", new Vec3i(2, 1, 0));
        items.PickUpItem(oreB.Id, dwarf.Id, dwarf.Position.Position);
        var fuel = items.CreateItem(ItemDefIds.CoalOre, "coal", new Vec3i(3, 1, 0));
        items.PickUpItem(fuel.Id, dwarf.Id, dwarf.Position.Position);

        Assert.True(discovery.IsRecipeUnlocked("make_iron_bar"));
        Assert.Contains(ItemDefIds.IronBar, discovery.GetCraftableItems());
        Assert.Equal(ItemDefIds.CoalOre, discovery.GetDiscoveredBy("make_iron_bar"));
    }

    [Fact]
    public void DiscoverySystem_Lists_Derived_Plank_Output_From_Log_Selector()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();
        var dwarf = new Dwarf(er.NextId(), "Woodworker", new Vec3i(10, 10, 0));
        er.Register(dwarf);

        var log = items.CreateItem(ItemDefIds.Log, "wood", new Vec3i(1, 1, 0));
        items.PickUpItem(log.Id, dwarf.Id, dwarf.Position.Position);

        Assert.True(discovery.IsRecipeUnlocked("make_plank"));
        Assert.Contains(ItemDefIds.Plank, discovery.GetCraftableItems());
        Assert.Equal(ItemDefIds.Log, discovery.GetDiscoveredBy("make_plank"));
    }

    [Fact]
    public void DiscoverySystem_Logs_Unlock_Hut_And_Track_Current_Buildability()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();

        var firstLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(1, 1, 0));

        Assert.Equal(DiscoveryKnowledgeState.Unlocked, discovery.GetBuildingState(BuildingDefIds.House));
        Assert.True(discovery.IsBuildingUnlocked(BuildingDefIds.House));
        Assert.True(discovery.IsBuildingUnlocked(BuildingDefIds.CarpenterWorkshop));
        Assert.Equal(DiscoveryKnowledgeState.Unlocked, discovery.GetBuildingState(BuildingDefIds.CarpenterWorkshop));
        Assert.True(discovery.IsRecipeUnlocked("make_plank"));
        Assert.Equal(ItemDefIds.Log, discovery.GetDiscoveredBy(BuildingDefIds.House));
        Assert.Equal(ItemDefIds.Log, discovery.GetDiscoveredBy(BuildingDefIds.CarpenterWorkshop));

        var secondLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(2, 1, 0));
        var box = new Box(er.NextId(), new Vec3i(6, 6, 0));
        er.Register(box);
        items.StoreItemInBox(firstLog.Id, box);
        items.StoreItemInBox(secondLog.Id, box);

        Assert.Equal(DiscoveryKnowledgeState.BuildableNow, discovery.GetBuildingState(BuildingDefIds.House));
    }

    [Fact]
    public void DiscoverySystem_BuildableNow_Requires_Stored_Construction_Inputs()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();

        var firstLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(1, 1, 0));
        var secondLog = items.CreateItem(ItemDefIds.Log, MaterialIds.Wood, new Vec3i(2, 1, 0));

        Assert.Equal(DiscoveryKnowledgeState.Unlocked, discovery.GetBuildingState(BuildingDefIds.House));

        var boxPos = new Vec3i(6, 6, 0);
        var box = new Box(er.NextId(), boxPos);
        er.Register(box);

        items.StoreItemInBox(firstLog.Id, box);
        items.StoreItemInBox(secondLog.Id, box);

        Assert.Equal(DiscoveryKnowledgeState.BuildableNow, discovery.GetBuildingState(BuildingDefIds.House));
    }

    [Fact]
    public void DiscoverySystem_Partial_Discovery_Uses_Known_State()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var discovery = sim.Context.Get<DiscoverySystem>();

        items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, new Vec3i(1, 1, 0));

        Assert.Equal(DiscoveryKnowledgeState.Known, discovery.GetBuildingState(BuildingDefIds.Kitchen));
        Assert.Equal(DiscoveryKnowledgeState.Known, discovery.GetBuildingState(BuildingDefIds.Still));
    }

    [Fact]
    public void BuildingSystem_Rejects_Undiscovered_Buildings_Even_When_Called_Directly()
    {
        var (sim, _, _, _, items) = TestFixtures.BuildFullSim();
        var rejections = new List<BuildingPlacementRejectedEvent>();
        sim.Context.EventBus.On<BuildingPlacementRejectedEvent>(rejection => rejections.Add(rejection));

        items.CreateItem(ItemDefIds.GraniteBoulder, MaterialIds.Granite, new Vec3i(1, 1, 0));

        sim.Context.Commands.Dispatch(new PlaceBuildingCommand(BuildingDefIds.Kitchen, new Vec3i(10, 10, 0)));

        var rejection = Assert.Single(rejections);
        Assert.Equal(BuildingDefIds.Kitchen, rejection.BuildingDefId);
        Assert.Equal("Building not discovered yet.", rejection.Reason);
    }
}
