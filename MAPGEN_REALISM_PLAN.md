# MAPGEN Realism Plan: Geology + Vegetation Overhaul

## 1. Goals

Improve map realism while keeping deterministic generation:

1. Remove artificial patterns (always-horizontal rivers, edge-only trees).
2. Introduce coherent terrain and hydrology using proper noise fields and flow accumulation.
3. Tie vegetation and resources to climate, soil, geology, slope, and water.
4. Preserve performance and backward compatibility via feature flags.

## 2. Current Gaps (Observed)

1. **Rivers are synthetic lines**:
   - Region rivers are currently axis-aligned bands.
   - Local streams are horizontal bands at fixed offsets.
2. **Terrain is not geomorphologically coherent**:
   - No elevation DEM pipeline with erosion/flow.
   - Outcrops are random points, not tied to structural geology.
3. **Vegetation is distribution-biased**:
   - Trees are mostly spawned in edge feature bands.
   - No clustering, no riparian corridors, no slope/soil/water constraints.
4. **Geology is coarse labels only**:
   - `GeologyProfileId` exists but does not drive stratified local materials strongly.

## 3. High-Level Architecture

Add a new realism stack behind flags:

1. `WorldRealismGenerator` (macro fields and tectonic context).
2. `RegionTerrainGenerator` (high-res DEM + drainage + eco-zones).
3. `LocalTerrainSynthesizer` (playable map carving with geology/vegetation masks).

Keep existing generators as fallback.

## 4. Feature Flags

Add runtime toggles:

1. `WorldGen.RealismV2` (master switch).
2. `WorldGen.HydrologyV2`.
3. `WorldGen.VegetationV2`.
4. `WorldGen.GeologyStrataV2`.

Default: all off initially, then gradually enable in ContentEditor previews first.

## 5. Data Model Upgrades

## 5.1 World Tile fields

Extend `GeneratedWorldTile` with:

1. `Continentalness`
2. `Erosion`
3. `PeaksValleys`
4. `PlateStress`
5. `BaseRockType`
6. `RunoffPotential`

## 5.2 Region Tile fields

Extend `GeneratedRegionTile` with:

1. `Elevation`
2. `FlowAccumulation`
3. `ChannelDepth`
4. `SoilDepth`
5. `Groundwater`
6. `CanopyDensity`
7. `BedrockExposure`

## 5.3 Local metadata

Add local-side metadata (sidecar if needed):

1. `SurfaceStrataId`
2. `SoilTypeId`
3. `MoistureClass`
4. `VegetationClass`
5. `OreChance`

## 6. Generation Pipeline (Realism V2)

## Phase A: Noise Foundation

1. Add deterministic coherent noise utilities:
   - Value/simplex/fBm, ridged noise, domain warp.
2. Standardize octave/lacunarity/gain presets for world/region/local scales.
3. Keep all noise seeded from hierarchical coordinates.

## Phase B: Macro Terrain + Climate

1. Build world DEM from:
   - Continentalness + uplift + erosion + peaks/valleys.
2. Temperature model:
   - Latitude gradient + altitude lapse + moisture moderation.
3. Moisture model:
   - Prevailing wind + rain-shadow + coastal humidity.
4. Compute runoff potential and drainage basins.

## Phase C: River Network

1. Compute flow direction (D8 or similar) on world/region DEM.
2. Compute flow accumulation.
3. Spawn channels from accumulation thresholds (not fixed straight bands).
4. Route rivers to basin exits/ocean/lakes with continuity constraints.
5. Ensure parent-child consistency:
   - Region channels inherit from world channel paths.
   - Local streams derive from region inflow/outflow points.

## Phase D: Geology + Strata

1. Generate tectonic stress zones and base rock distributions.
2. Derive strata stack per world/region:
   - Surface sediment, shallow bedrock, deep intrusive/metamorphic.
3. Convert strata to local material IDs and ore probability masks.
4. Tie exposed rock to slope, erosion, and channel incision.

## Phase E: Vegetation Ecology

1. Compute Potential Natural Vegetation (PNV) per tile from:
   - Temperature, moisture, soil depth, slope, and disturbance.
2. Spawn vegetation using clustered placement:
   - Poisson/blue-noise + biome-dependent patch growth.
3. Add riparian corridors:
   - Higher canopy and wetland vegetation near channels/floodplains.
4. Add elevation gradients:
   - Treeline effects, alpine exposure, dry leeward reduction.

## Phase F: Local Playability Constraints

1. Preserve guaranteed playable center and passable borders.
2. Preserve staircase and fortress startup viability.
3. Carve micro-drainage naturally while respecting gameplay constraints.

## 7. Milestones

## Milestone R1: Infrastructure

1. Implement noise module and test determinism.
2. Add new fields to world/region tiles (nullable/backward-safe).
3. Add feature flags and config schema.

## Milestone R2: Hydrology Rewrite

1. Replace line rivers with flow-accumulation routing in region/local.
2. Add river continuity tests across scales.
3. Add river overlays in MapPreview.

## Milestone R3: Geology Strata

1. Introduce strata stack generation and bedrock exposure.
2. Tie outcrops/ore potential to geology masks.
3. Add geology preview overlays and stats.

## Milestone R4: Vegetation Rewrite

1. Replace edge-band tree spawning with eco-model + clustered placement.
2. Add riparian/tree density gradients.
3. Add biome-aware tree species groups (future-ready IDs).

## Milestone R5: Tuning + Budgets

1. Run seed sweeps for distribution sanity.
2. Tune thresholds per biome family.
3. Lock initial budgets and snapshots.

## Milestone R6: Rollout

1. Enable in ContentEditor by default.
2. Keep game runtime behind flag for one release cycle.
3. Promote to default after parity checks.

## 8. Validation & QA

## 8.1 Determinism tests

1. Same seed + coords -> identical world/region/local hashes.
2. Hash snapshots for representative seeds.

## 8.2 Realism invariants

1. Rivers mostly flow downhill by DEM.
2. River width correlates with flow accumulation.
3. Tree density correlates positively with moisture and soil depth.
4. Bedrock exposure correlates positively with slope and erosion.

## 8.3 Distribution tests (property/sweep)

1. Water coverage per biome within target ranges.
2. Tree coverage not concentrated only at edges.
3. Ore potential variance across geology profiles.

## 8.4 Gameplay invariants

1. Passable borders and center guarantees remain true.
2. Fortress bootstrap remains stable across sampled seeds.

## 9. ContentEditor Enhancements

Add overlays for rapid debugging:

1. World: continentalness, elevation, moisture, runoff.
2. Region: flow accumulation, channel depth, soil depth, canopy density.
3. Local: vegetation class, strata class, ore potential.
4. Comparative regenerate panel:
   - run N seeds and display trees/water/walls/ore summary table.

## 10. Performance Targets

1. World generation: <= 300 ms (realism mode, default size).
2. Region generation: <= 80 ms.
3. Local generation: <= 40 ms.
4. Additional memory overhead: <= 2x current temporary buffers.

## 11. Risks & Mitigations

1. **Risk:** Complexity spike.
   - Mitigation: phased milestones with flags and baseline snapshots.
2. **Risk:** Overfitting to a few seeds.
   - Mitigation: automated sweeps over large seed ranges.
3. **Risk:** Gameplay regressions.
   - Mitigation: preserve playability post-pass and keep fallback path.

## 12. Immediate Execution Plan (Next 2 Sprints)

## Sprint 1

1. Implement noise utility module and hydrology core (flow + accumulation).
2. Replace region line-river generation with routed channels.
3. Add MapPreview river-flow overlay.

## Sprint 2

1. Implement local DEM-driven stream carving.
2. Replace edge-band tree spawn with clustered ecological placement.
3. Add vegetation/geology diagnostics and run seed sweeps.
