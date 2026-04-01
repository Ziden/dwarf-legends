namespace DwarfFortress.WorldGen.Geology;

public readonly record struct StrataLayer(
    string RockTypeId,
    int ThicknessMin,
    int ThicknessMax);
