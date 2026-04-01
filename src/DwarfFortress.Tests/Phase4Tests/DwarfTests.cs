using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase4Tests;

public sealed class DwarfTests
{
    private static Dwarf CreateDwarf()
        => new Dwarf(1, "Urist", new Vec3i(3, 4, 0));

    [Fact]
    public void Dwarf_Has_Position_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Position);
        Assert.Equal(new Vec3i(3, 4, 0), dwarf.Position.Position);
    }

    [Fact]
    public void Dwarf_Has_Stats_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Stats);
    }

    [Fact]
    public void Dwarf_Has_Skills_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Skills);
    }

    [Fact]
    public void Dwarf_Has_Needs_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Needs);
    }

    [Fact]
    public void Dwarf_Has_Mood_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Mood);
    }

    [Fact]
    public void Dwarf_Has_Thoughts_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Thoughts);
    }

    [Fact]
    public void Dwarf_Has_Labors_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Labors);
    }

    [Fact]
    public void Dwarf_Has_Health_Component()
    {
        var dwarf = CreateDwarf();

        Assert.NotNull(dwarf.Health);
    }

    [Fact]
    public void Dwarf_Is_Alive_After_Construction()
    {
        var dwarf = CreateDwarf();

        Assert.True(dwarf.IsAlive);
    }

    [Fact]
    public void Dwarf_Kill_Sets_IsAlive_False()
    {
        var dwarf = CreateDwarf();

        dwarf.Kill();

        Assert.False(dwarf.IsAlive);
    }

    [Fact]
    public void Dwarf_FirstName_Matches_Constructor_Arg()
    {
        var dwarf = CreateDwarf();

        Assert.Equal("Urist", dwarf.FirstName);
    }

    [Fact]
    public void Dwarf_DefId_Is_Dwarf_Constant()
    {
        var dwarf = CreateDwarf();

        Assert.Equal(DefIds.Dwarf, dwarf.DefId);
    }

    [Fact]
    public void Dwarf_Needs_Start_At_Full()
    {
        var dwarf = CreateDwarf();

        Assert.Equal(1.0f, dwarf.Needs.Hunger.Level);
        Assert.Equal(1.0f, dwarf.Needs.Thirst.Level);
        Assert.Equal(1.0f, dwarf.Needs.Sleep.Level);
    }

    [Fact]
    public void Dwarf_Health_IsConscious_True_At_Full_Health()
    {
        var dwarf = CreateDwarf();

        Assert.True(dwarf.Health.IsConscious);
    }
}
