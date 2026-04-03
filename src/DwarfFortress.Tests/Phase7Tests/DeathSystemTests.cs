using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Items;
using DwarfFortress.GameLogic.Systems;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class DeathSystemTests
{
    [Fact]
    public void DeathSystem_Dehydration_Creates_Corpse_With_Contained_Items()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(9, 9, 0);
        var creature = new Creature(er.NextId(), DefIds.Elk, pos, maxHealth: 85f);
        er.Register(creature);

        var carried = items.CreateItem(ItemDefIds.Bone, string.Empty, new Vec3i(3, 3, 0));
        items.PickUpItem(carried.Id, creature.Id, pos);
        creature.Needs.Thirst.SetLevel(0f, 6f * 60f);

        sim.Tick(0.1f);

        Assert.False(creature.IsAlive);

        var corpse = items.GetItemsAt(pos).Single(item => item.DefId == ItemDefIds.Corpse);
        Assert.NotNull(corpse.Components.TryGet<CorpseComponent>());

        var contents = items.GetItemsInItem(corpse.Id).ToList();
        Assert.Contains(contents, item => item.Id == carried.Id);
        Assert.DoesNotContain(items.GetItemsAt(pos), item => item.Id == carried.Id);

        var tile = queries.QueryTile(pos);
        var corpseView = Assert.Single(tile.Items.Where(item => item.DefId == ItemDefIds.Corpse));
        Assert.NotNull(corpseView.Corpse);
        Assert.NotNull(corpseView.Storage);
        Assert.Equal("Corpse of Elk", corpseView.DisplayName);
        Assert.Equal(DefIds.Elk, corpseView.Corpse!.FormerDefId);
        Assert.Contains(corpseView.Storage!.Contents, item => item.Id == carried.Id);
    }

    [Fact]
    public void DeathSystem_SaveLoad_Restores_Corpse_Rot_And_Contents()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();

        var pos = new Vec3i(12, 8, 0);
        var creature = new Creature(er.NextId(), DefIds.Elk, pos, maxHealth: 85f);
        er.Register(creature);

        var carried = items.CreateItem(ItemDefIds.Bone, string.Empty, new Vec3i(1, 1, 0));
        items.PickUpItem(carried.Id, creature.Id, pos);
        creature.Needs.Thirst.SetLevel(0f, 6f * 60f);

        sim.Tick(0.1f);
        sim.Tick(120f);

        var corpse = items.GetItemsAt(pos).Single(item => item.DefId == ItemDefIds.Corpse);
        var rotBefore = corpse.Components.Get<RotComponent>().Progress;
        Assert.True(rotBefore > 0f);

        var json = sim.Save();

        var (sim2, _, _, _, items2) = TestFixtures.BuildFullSim();
        sim2.Load(json);

        var restoredCorpse = items2.GetAllItems().Single(item => item.DefId == ItemDefIds.Corpse);
        var restoredRot = restoredCorpse.Components.Get<RotComponent>().Progress;
        Assert.InRange(restoredRot, rotBefore - 0.001f, rotBefore + 0.001f);
        Assert.Contains(items2.GetItemsInItem(restoredCorpse.Id), item => item.DefId == ItemDefIds.Bone);
    }

    [Fact]
    public void DeathSystem_Stores_BundleAuthoredCreatureDeathDrops_Inside_Corpse()
    {
        var (sim, _, er, _, items) = TestFixtures.BuildFullSim();
        var queries = sim.Context.Get<WorldQuerySystem>();

        var pos = new Vec3i(14, 10, 0);
        var creature = new Creature(er.NextId(), DefIds.Elk, pos, maxHealth: 85f);
        er.Register(creature);
        creature.Needs.Thirst.SetLevel(0f, 6f * 60f);

        sim.Tick(0.1f);

        var topLevelItems = items.GetItemsAt(pos).ToList();
        var corpse = Assert.Single(topLevelItems, item => item.DefId == ItemDefIds.Corpse);
        var corpseContents = items.GetItemsInItem(corpse.Id).ToList();

        Assert.Single(topLevelItems);
        Assert.Equal(2, corpseContents.Count(item => item.DefId == ItemDefIds.RawMeat));
        Assert.Equal(1, corpseContents.Count(item => item.DefId == ItemDefIds.Leather));
        Assert.Equal(2, corpseContents.Count(item => item.DefId == ItemDefIds.Bone));

        var corpseView = Assert.Single(queries.QueryTile(pos).Items);
        Assert.NotNull(corpseView.Storage);
        Assert.Equal(5, corpseView.Storage!.StoredItemCount);
        Assert.Equal(2, corpseView.Storage.Contents.Count(item => item.DefId == ItemDefIds.RawMeat));
    }
}
