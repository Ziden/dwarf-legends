using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data.Defs;
using DwarfFortress.GameLogic.World;

namespace DwarfFortress.GameLogic.Tests.Phase7Tests;

public sealed class TerrainClearedGroundResolverTests
{
    [Fact]
    public void TerrainClearedGroundResolver_UsesDirtBelowForWoodBackedTiles()
    {
        var result = TerrainClearedGroundResolver.Resolve(
            pos: new Vec3i(10, 12, 0),
            currentMaterialId: MaterialIds.Wood,
            isWoodMaterial: materialId => string.Equals(materialId, MaterialIds.Wood, StringComparison.OrdinalIgnoreCase),
            isDirtMaterial: materialId => string.Equals(materialId, "loam", StringComparison.OrdinalIgnoreCase),
            tryGetTerrainMaterial: pos => pos == new Vec3i(10, 12, 1)
                ? new TerrainGroundMaterialSample("loam", TerrainGroundMaterialKind.Dirt)
                : null);

        Assert.Equal(TileDefIds.Soil, result.TileDefId);
        Assert.Equal("loam", result.MaterialId);
    }

    [Fact]
    public void TerrainClearedGroundResolver_FallsBackToNearestStoneRingWhenNoDirtSourceExists()
    {
        var result = TerrainClearedGroundResolver.Resolve(
            pos: new Vec3i(5, 5, 0),
            currentMaterialId: MaterialIds.Wood,
            isWoodMaterial: materialId => string.Equals(materialId, MaterialIds.Wood, StringComparison.OrdinalIgnoreCase),
            isDirtMaterial: _ => false,
            tryGetTerrainMaterial: pos => pos == new Vec3i(6, 5, 0)
                ? new TerrainGroundMaterialSample(MaterialIds.Granite, TerrainGroundMaterialKind.Stone)
                : null);

        Assert.Equal(TileDefIds.StoneFloor, result.TileDefId);
        Assert.Equal(MaterialIds.Granite, result.MaterialId);
    }
}
