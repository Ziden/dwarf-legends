using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;
using DwarfFortress.GameLogic.World;
using Xunit;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class EmoteFeedbackSystemTests
{
    [Fact]
    public void EmoteFeedbackSystem_Shows_Persistent_Thirst_Balloon_For_Dwarf_And_Clears_When_Satisfied()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Urist", new Vec3i(8, 8, 0));
        dwarf.Needs.Hunger.SetLevel(1f);
        dwarf.Needs.Thirst.SetLevel(0.02f);
        dwarf.Needs.Sleep.SetLevel(1f);
        er.Register(dwarf);

        sim.Tick(0.1f);

        var currentEmote = Assert.IsType<Emote>(dwarf.Emotes.CurrentEmote);
        Assert.Equal(EmoteIds.NeedWater, currentEmote.Id);
        Assert.True(currentEmote.IsPersistent);
        Assert.Equal(EmoteVisualStyle.Balloon, currentEmote.VisualStyle);
        Assert.Equal(EmoteCategory.Need, currentEmote.Category);

        dwarf.Needs.Thirst.Satisfy(1f);

        sim.Tick(0.1f);

        Assert.NotEqual(EmoteIds.NeedWater, dwarf.Emotes.CurrentEmote?.Id);
    }

    [Fact]
    public void EmoteFeedbackSystem_Shows_Persistent_Hunger_Balloon_For_Creature()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var elk = new Creature(er.NextId(), DefIds.Elk, new Vec3i(10, 10, 0), maxHealth: 85f);
        elk.Needs.Hunger.SetLevel(0.01f);
        elk.Needs.Thirst.SetLevel(1f);
        er.Register(elk);

        sim.Tick(0.1f);

        var currentEmote = Assert.IsType<Emote>(elk.Emotes.CurrentEmote);
        Assert.Equal(EmoteIds.NeedFood, currentEmote.Id);
        Assert.True(currentEmote.IsPersistent);
        Assert.Equal(EmoteVisualStyle.Balloon, currentEmote.VisualStyle);
        Assert.Equal(EmoteCategory.Need, currentEmote.Category);
    }

    [Fact]
    public void EmoteFeedbackSystem_Shows_Short_Mood_Balloons_For_Happiness_Gains_And_Losses()
    {
        var (sim, _, er, _, _) = TestFixtures.BuildFullSim();

        var dwarf = new Dwarf(er.NextId(), "Rigoth", new Vec3i(12, 12, 0));
        dwarf.Needs.Hunger.SetLevel(1f);
        dwarf.Needs.Thirst.SetLevel(1f);
        dwarf.Needs.Sleep.SetLevel(1f);
        er.Register(dwarf);

        sim.Tick(0.1f);

        dwarf.Thoughts.AddThought(new Thought("pleasant_note", "A brief pleasant moment.", 0.06f, duration: 30f));

        sim.Tick(0.1f);

        var positiveEmote = Assert.IsType<Emote>(dwarf.Emotes.CurrentEmote);
        Assert.Equal(EmoteIds.MoodUp, positiveEmote.Id);
        Assert.Equal(Mood.Content, dwarf.Mood.Current);
        Assert.False(positiveEmote.IsPersistent);
        Assert.Equal(EmoteVisualStyle.Balloon, positiveEmote.VisualStyle);
        Assert.Equal(EmoteCategory.Mood, positiveEmote.Category);
        Assert.InRange(positiveEmote.Duration, 2.3f, 2.5f);

        dwarf.Thoughts.RemoveThought("pleasant_note");
        dwarf.Thoughts.AddThought(new Thought("bad_note", "A rough setback.", -0.35f, duration: 30f));

        sim.Tick(0.1f);

        var negativeEmote = Assert.IsType<Emote>(dwarf.Emotes.CurrentEmote);
        Assert.Equal(EmoteIds.MoodDown, negativeEmote.Id);
        Assert.True(negativeEmote.Intensity > positiveEmote.Intensity);
        Assert.False(negativeEmote.IsPersistent);
        Assert.InRange(negativeEmote.Duration, 2.3f, 2.5f);
    }

    [Fact]
    public void EmoteComponent_Restores_Persistent_Need_Balloon_After_Temporary_Mood_Pulse_Expires()
    {
        var emotes = new EmoteComponent();

        emotes.SetPersistentEmote(EmoteIds.NeedWater, intensity: 0.7f, EmoteVisualStyle.Balloon, EmoteCategory.Need);
        emotes.SetEmote(EmoteIds.MoodUp, duration: 1f, intensity: 0.4f, EmoteVisualStyle.Balloon, EmoteCategory.Mood);

        Assert.Equal(EmoteIds.MoodUp, emotes.CurrentEmote?.Id);

        emotes.Tick(1.1f);

        var currentEmote = Assert.IsType<Emote>(emotes.CurrentEmote);
        Assert.Equal(EmoteIds.NeedWater, currentEmote.Id);
        Assert.True(currentEmote.IsPersistent);
    }
}