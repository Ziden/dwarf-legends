using System.Collections.Generic;

namespace DwarfFortress.WorldGen.Geology;

public sealed record StrataProfile(
    string GeologyProfileId,
    int SeedSalt,
    IReadOnlyList<StrataLayer> Layers,
    float AquiferDepthFraction = 0f);
