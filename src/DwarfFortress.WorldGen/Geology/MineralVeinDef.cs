namespace DwarfFortress.WorldGen.Geology;

public enum VeinShape : byte
{
    Cluster = 0,
    Layer = 1,
    Vein = 2,
    Scattered = 3,
}

public readonly record struct MineralVeinDef(
    string MaterialId,
    string ResourceItemDefId,
    string ResourceFormRole,
    VeinShape Shape,
    float Frequency,
    string RequiredRockType,
    int SizeMin,
    int SizeMax)
{
    public string OreId => ResourceItemDefId;
}
