using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Tests;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase4Tests;

public sealed class EntityRegistryTests
{
    private static (EntityRegistry registry, GameContext ctx) CreateRegistry()
    {
        var (ctx, _, _) = TestFixtures.CreateContext();
        var registry = new EntityRegistry();
        registry.Initialize(ctx);
        return (registry, ctx);
    }

    [Fact]
    public void Register_Then_TryGetById_Returns_Entity()
    {
        var (registry, _) = CreateRegistry();
        var dwarf = new Dwarf(1, "Urist", new Vec3i(0, 0, 0));

        registry.Register(dwarf);

        var result = registry.TryGetById(1);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public void Register_Emits_EntitySpawnedEvent()
    {
        var (registry, ctx) = CreateRegistry();
        int? spawnedId = null;
        ctx.EventBus.On<EntitySpawnedEvent>(e => spawnedId = e.EntityId);

        registry.Register(new Dwarf(42, "Bob", new Vec3i(0, 0, 0)));

        Assert.Equal(42, spawnedId);
    }

    [Fact]
    public void Kill_Marks_Entity_As_Dead()
    {
        var (registry, _) = CreateRegistry();
        var dwarf = new Dwarf(5, "Urist", new Vec3i(0, 0, 0));
        registry.Register(dwarf);

        registry.Kill(5, "test");

        Assert.False(dwarf.IsAlive);
    }

    [Fact]
    public void Kill_Emits_EntityKilledEvent()
    {
        var (registry, ctx) = CreateRegistry();
        int? killedId = null;
        string? cause = null;
        ctx.EventBus.On<EntityKilledEvent>(e => { killedId = e.EntityId; cause = e.Cause; });
        registry.Register(new Dwarf(7, "Urist", new Vec3i(0, 0, 0)));

        registry.Kill(7, "cave_in");

        Assert.Equal(7, killedId);
        Assert.Equal("cave_in", cause);
    }

    [Fact]
    public void GetAlive_Excludes_Dead_Entities()
    {
        var (registry, _) = CreateRegistry();
        registry.Register(new Dwarf(1, "Alive", new Vec3i(0, 0, 0)));
        registry.Register(new Dwarf(2, "Dead",  new Vec3i(1, 0, 0)));
        registry.Kill(2, "test");

        var alive = registry.GetAlive<Dwarf>().ToList();

        Assert.Single(alive);
        Assert.Equal(1, alive[0].Id);
    }

    [Fact]
    public void GetAll_Includes_Dead_Entities()
    {
        var (registry, _) = CreateRegistry();
        registry.Register(new Dwarf(1, "Alive", new Vec3i(0, 0, 0)));
        registry.Register(new Dwarf(2, "Dead",  new Vec3i(1, 0, 0)));
        registry.Kill(2, "test");

        var all = registry.GetAll<Dwarf>().ToList();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void TryGetById_Generic_Returns_False_For_Wrong_Type()
    {
        var (registry, _) = CreateRegistry();
        registry.Register(new Dwarf(99, "Urist", new Vec3i(0, 0, 0)));

        bool found = registry.TryGetById<Creature>(99, out var creature);

        Assert.False(found);
        Assert.Null(creature);
    }

    [Fact]
    public void TryGetById_Generic_Returns_True_For_Correct_Type()
    {
        var (registry, _) = CreateRegistry();
        registry.Register(new Dwarf(10, "Urist", new Vec3i(0, 0, 0)));

        bool found = registry.TryGetById<Dwarf>(10, out var dwarf);

        Assert.True(found);
        Assert.NotNull(dwarf);
    }

    [Fact]
    public void Register_Duplicate_Id_Throws()
    {
        var (registry, _) = CreateRegistry();
        registry.Register(new Dwarf(1, "First", new Vec3i(0, 0, 0)));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new Dwarf(1, "Second", new Vec3i(0, 0, 0))));
    }
}
