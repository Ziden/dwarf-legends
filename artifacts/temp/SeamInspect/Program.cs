using System.Text;
using DwarfFortress.GameLogic.World;
using DwarfFortress.WorldGen.Analysis;
using DwarfFortress.WorldGen.Generation;
using DwarfFortress.WorldGen.Local;

var service = new MapGenerationService();
var settings = new MapGenerationSettings(64, 64, 32, 32, true, 125);
var localSettings = new LocalGenerationSettings(48, 48, 8);
var worldCoord = new WorldCoord(45, 25);
var region = service.GetOrCreateRegion(42, worldCoord, settings);
var leftCoord = new RegionCoord(45, 25, 9, 8);
var rightCoord = new RegionCoord(45, 25, 10, 8);
var left = service.GetOrCreateLocal(42, leftCoord, localSettings, settings);
var right = service.GetOrCreateLocal(42, rightCoord, localSettings, settings);
var sb = new StringBuilder();

sb.AppendLine($"left region tile: edges={region.GetTile(9, 8).RiverEdges} hasRiver={region.GetTile(9, 8).HasRiver} order={region.GetTile(9, 8).RiverOrder} discharge={region.GetTile(9, 8).RiverDischarge:0.###}");
sb.AppendLine($"right region tile: edges={region.GetTile(10, 8).RiverEdges} hasRiver={region.GetTile(10, 8).HasRiver} order={region.GetTile(10, 8).RiverOrder} discharge={region.GetTile(10, 8).RiverDischarge:0.###}");
sb.AppendLine();

for (var y = 0; y < 48; y++)
{
    var a = left.GetTile(47, y, 0);
    var b = right.GetTile(0, y, 0);
    var mismatch = EmbarkBoundaryContinuity.CompareTiles(a, b);
    if (mismatch == EmbarkBoundaryMismatchKind.None)
        continue;

    sb.Append("y=").Append(y)
      .Append(" mismatch=").Append(mismatch)
      .Append(" | left tile=").Append(a.TileDefId).Append(" level=").Append(a.FluidLevel).Append(" fluid=").Append(a.FluidType)
      .Append(" | right tile=").Append(b.TileDefId).Append(" level=").Append(b.FluidLevel).Append(" fluid=").Append(b.FluidType)
      .AppendLine();
}

Console.Write(sb.ToString());