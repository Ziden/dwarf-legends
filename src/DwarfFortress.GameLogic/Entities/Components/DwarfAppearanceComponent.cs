using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Entities.Components;

public enum DwarfHairType
{
    Bald,
    Crop,
    Swept,
    Shaggy,
    Crest,
    Braided,
}

public enum DwarfHairColor
{
    Coal,
    Chestnut,
    Copper,
    Blond,
    Ash,
    Silver,
}

public enum DwarfBeardType
{
    Clean,
    Short,
    Full,
    Braided,
    Forked,
    Mutton,
}

public enum DwarfEyeType
{
    Dot,
    Narrow,
    Wide,
    HeavyBrow,
    Bright,
}

public enum DwarfNoseType
{
    Button,
    Broad,
    Long,
    Hooked,
}

public enum DwarfMouthType
{
    Neutral,
    Smile,
    Smirk,
    Frown,
    Open,
}

public enum DwarfFaceType
{
    Round,
    Square,
    Long,
    Wide,
}

public sealed class DwarfAppearanceComponent
{
    /// <summary>Height in cm. Used for weight calculation. Range: 100-160 for dwarves.</summary>
    public float Height { get; set; } = 130f;

    public DwarfHairType HairType { get; set; }
    public DwarfHairColor HairColor { get; set; }
    public DwarfBeardType BeardType { get; set; }
    public DwarfHairColor BeardColor { get; set; }
    public DwarfEyeType EyeType { get; set; }
    public DwarfNoseType NoseType { get; set; }
    public DwarfMouthType MouthType { get; set; }
    public DwarfFaceType FaceType { get; set; }

    public string Signature
        => $"{(int)HairType}:{(int)HairColor}:{(int)BeardType}:{(int)BeardColor}:{(int)EyeType}:{(int)NoseType}:{(int)MouthType}:{(int)FaceType}";

    public static DwarfAppearanceComponent CreateDefault(int id, string name, Vec3i spawnPos)
    {
        var component = new DwarfAppearanceComponent();
        component.RandomizeDistinct(CreateSeed(id, name, spawnPos), null);
        return component;
    }

    public void RandomizeDistinct(int baseSeed, ISet<string>? usedSignatures)
    {
        for (var attempt = 0; attempt < 256; attempt++)
        {
            var candidate = Generate(baseSeed + (attempt * 977));
            if (usedSignatures is not null && !usedSignatures.Add(candidate.Signature))
                continue;

            Apply(candidate);
            return;
        }

        var fallback = Generate(baseSeed);
        usedSignatures?.Add(fallback.Signature);
        Apply(fallback);
    }

    public static int CreateSeed(int id, string name, Vec3i spawnPos)
        => StableHash($"{id}:{name}:{spawnPos.X}:{spawnPos.Y}:{spawnPos.Z}");

    private void Apply(DwarfAppearanceComponent other)
    {
        Height = other.Height;
        HairType = other.HairType;
        HairColor = other.HairColor;
        BeardType = other.BeardType;
        BeardColor = other.BeardColor;
        EyeType = other.EyeType;
        NoseType = other.NoseType;
        MouthType = other.MouthType;
        FaceType = other.FaceType;
    }

    private static DwarfAppearanceComponent Generate(int seed)
    {
        var hash = StableHash($"dwarf_appearance:{seed}");
        // Height: 100-160cm based on hash
        var height = 100f + (Math.Abs(hash) % 61);
        return new DwarfAppearanceComponent
        {
            Height = height,
            HairType = PickEnum<DwarfHairType>(hash, 0),
            HairColor = PickEnum<DwarfHairColor>(hash, 5),
            BeardType = PickEnum<DwarfBeardType>(hash, 10),
            BeardColor = PickEnum<DwarfHairColor>(hash, 15),
            EyeType = PickEnum<DwarfEyeType>(hash, 20),
            NoseType = PickEnum<DwarfNoseType>(hash, 25),
            MouthType = PickEnum<DwarfMouthType>(hash, 30),
            FaceType = PickEnum<DwarfFaceType>(hash, 3),
        };
    }

    private static TEnum PickEnum<TEnum>(int hash, int shift) where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var index = Math.Abs((hash >> shift) ^ (hash << (shift % 7))) % values.Length;
        return values[index];
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash;
        }
    }
}