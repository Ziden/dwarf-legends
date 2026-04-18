namespace DwarfFortress.WorldGen.Maps;

public static partial class EmbarkGenerator
{
    private static readonly IEmbarkGenerationPass[] GenerationPasses =
    [
        new SurfaceShapePass(),
        new UndergroundStructurePass(),
        new HydrologyPass(),
        new EcologyPass(),
        new HydrologyPolishPass(),
        new CivilizationOverlayPass(),
        new PlayabilityPass(),
        new PopulationPass(),
    ];

    private interface IEmbarkGenerationPass
    {
        EmbarkGenerationStageId StageId { get; }

        void Execute(LocalGenerationContext context);
    }

    private sealed class SurfaceShapePass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.SurfaceShape;

        public void Execute(LocalGenerationContext context)
            => RunSurfaceShapeStage(context);
    }

    private sealed class UndergroundStructurePass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.UndergroundStructure;

        public void Execute(LocalGenerationContext context)
            => RunUndergroundStructureStage(context);
    }

    private sealed class HydrologyPass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.Hydrology;

        public void Execute(LocalGenerationContext context)
            => RunHydrologyStage(context);
    }

    private sealed class EcologyPass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.Ecology;

        public void Execute(LocalGenerationContext context)
            => RunEcologyStage(context);
    }

    private sealed class HydrologyPolishPass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.HydrologyPolish;

        public void Execute(LocalGenerationContext context)
            => RunHydrologyPolishStage(context);
    }

    private sealed class CivilizationOverlayPass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.CivilizationOverlay;

        public void Execute(LocalGenerationContext context)
            => RunCivilizationOverlayStage(context);
    }

    private sealed class PlayabilityPass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.Playability;

        public void Execute(LocalGenerationContext context)
            => RunPlayabilityStage(context);
    }

    private sealed class PopulationPass : IEmbarkGenerationPass
    {
        public EmbarkGenerationStageId StageId => EmbarkGenerationStageId.Population;

        public void Execute(LocalGenerationContext context)
            => RunPopulationStage(context);
    }
}