# MAPGEN Continuity + Realism Plan (World -> Region -> Local)

## 1. Why We Need a New Pass

Current generation has improved noise and hydrology, but continuity is still incomplete across 3 layers:

1. `WorldLayerGenerator` creates plausible macro bands, but no explicit world river graph.
2. `RegionLayerGenerator` computes flow inside each region independently, so cross-region river continuation is not guaranteed.
3. `LocalLayerGenerator` only passes coarse biases (`HasRiver`, `HasLake`, vegetation/resource/slope) into `EmbarkGenerator`.
4. `EmbarkGenerator` streams are still generated from local random sources (downhill-ish), so isolated/looping local rivers can appear.

This is why maps can feel non-DF-like: hydrology is not yet a strict parent->child inheritance chain.

## 2. Target Outcome (DF-Like Quality Bar)

We want a deterministic, layered world where:

1. Landforms are coherent at all scales.
2. Major rivers originate in highlands and continue across world/region/local boundaries.
3. Local embark water features are inherited from parent hydrology, not spawned independently.
4. Vegetation follows moisture, elevation, soil, and river corridors.
5. Gameplay constraints (safe embark center/borders) remain intact.

## 3. Core Design Rule

Each layer must **consume explicit constraints** from the parent layer, not infer them loosely.

Parent -> child contracts:

1. `World` provides macro DEM + basin graph + major river segments.
2. `Region` provides high-res DEM + channel network + boundary inflow/outflow descriptors.
3. `Local` provides tile-level terrain carved from region constraints with anchored stream entries/exits.

## 4. Data Contract Additions

## 4.1 World-level outputs

Add to world generation output (sidecar if needed):

1. `WorldElevation[x,y]`
2. `WorldRunoff[x,y]`
3. `WorldFlowDir[x,y]`
4. `WorldFlowAccum[x,y]`
5. `WorldRiverMask[x,y]`
6. `WorldRiverEdges[x,y]`: which cell borders have river continuity (`N,E,S,W`).

## 4.2 Region-level outputs

Add per region:

1. `RegionElevation[x,y]`
2. `RegionFlowDir[x,y]`
3. `RegionFlowAccum[x,y]`
4. `RegionRiverMask[x,y]`
5. `RegionBoundaryHydrology`: river entry/exit points with discharge estimates.
6. `RegionSoilDepth[x,y]`, `RegionGroundwater[x,y]`.

## 4.3 Local-level inputs

Extend `LocalGenerationSettings` (or region->local context object):

1. `RiverEntryPoints` (edge coordinates + flow magnitude)
2. `RiverExitPoints`
3. `ParentSlopeBias`
4. `ParentWetnessBias`
5. `ParentSoilDepthBias`

## 5. Implementation Phases

## Phase 0: Baseline Instrumentation

1. Add metrics collector for world/region/local:
   - river cell count
   - connected river component count
   - border crossing counts
   - isolated water patch count
2. Add CLI output for these metrics.

Acceptance:

1. We can quantify "isolated rivers" before making algorithm changes.

## Phase 1: Geology-First Macro Foundation

1. Build stable world DEM pipeline (continentalness + uplift + ridged mountains + erosion proxy).
2. Compute geology strata seeds from tectonic/ridge fields.
3. Expose geology as deterministic world masks.

Acceptance:

1. Adjacent world tiles share continuous elevation transitions.
2. Highlands correlate with geology exposure.

## Phase 2: True World Hydrology

1. Run world-scale D8 flow on full world DEM (single pass, not per-tile isolated).
2. Add depression handling:
   - pit fill or breach strategy to avoid dead-end noise basins everywhere.
3. Derive world river network from accumulation threshold.
4. Store river edge crossings (`N,E,S,W`) per world cell.

Acceptance:

1. Rivers mostly flow downhill.
2. River network crosses world-cell boundaries consistently.
3. Stream order/width proxy correlates with accumulation.

## Phase 3: Region Hydrology with Boundary Constraints

1. Region generator consumes parent world river edges and discharge.
2. Build high-res region DEM with boundary anchoring:
   - enforce entry/exit channels at matching border positions.
3. Run region flow and accumulation with constrained boundary conditions.
4. Generate lakes only where flow/sink logic supports them.

Acceptance:

1. Neighboring regions agree on shared-border river crossings.
2. No abrupt river termination at region edges unless true sink/lake outlet.

## Phase 4: Local Embark Hydrology Anchoring

1. Replace random stream source selection with constrained entry/exit carving.
2. If parent region says river crosses this cell, local map must contain crossing channel.
3. Secondary tributaries may be procedural, but anchored to main channel topology.
4. Keep embark safety rules:
   - center zone clear
   - border passable
   - staircase valid.

Acceptance:

1. Isolated local rivers drop sharply in seed sweeps.
2. Parent has-river cells produce visible local river logic consistently.

## Phase 5: Vegetation and Soil Ecology

1. Build soil depth and groundwater maps from hydrology + slope + geology.
2. Compute vegetation suitability from:
   - moisture
   - soil depth
   - temperature/elevation
   - floodplain proximity.
3. Use clustered placement (patch growth / blue noise), not pure random.
4. Add riparian corridor boosts.

Acceptance:

1. Tree density positively correlates with moisture/soil depth.
2. Riparian zones visibly greener than adjacent slopes.

## Phase 6: Tuning and Constraints

1. Run large seed sweeps.
2. Tune thresholds per macro biome.
3. Lock target ranges:
   - water coverage
   - tree coverage
   - rock exposure
   - passable area.

Acceptance:

1. Distributions stay inside targets for sampled seeds.
2. No frequent gameplay-breaking starts.

## Phase 7: Godot WorldGen Viewer Upgrade

1. Add overlay modes:
   - elevation
   - flow accumulation
   - river network
   - soil depth
   - vegetation suitability.
2. Add continuity debug views:
   - highlight mismatched border crossings between adjacent regions.
3. Add one-click seed stepping and histogram summaries.

Acceptance:

1. We can visually debug continuity without web tooling.

## 6. Testing Strategy (Non-Negotiable)

## 6.1 Determinism tests

1. Same seed + same coords -> identical hashes for world/region/local outputs.

## 6.2 Continuity tests

1. Adjacent world cells share coherent edge fields.
2. Adjacent region maps have matching border river crossings.
3. Local maps derived from adjacent region river cells preserve crossing continuity intent.

## 6.3 Realism tests

1. River downhill ratio above threshold.
2. Accumulation-width monotonic trend.
3. Vegetation-moisture correlation above threshold.

## 6.4 Gameplay tests

1. Embark center viability.
2. Border passability.
3. Fortress bootstrap success across sampled seeds.

## 7. Rollout Strategy

1. Gate behind flags:
   - `WorldGen.ContinuityV1`
   - `WorldGen.HydrologyAnchorsV1`
   - `WorldGen.EcologyV1`
2. Enable first in Godot Test World Gen mode.
3. Promote to default after seed-sweep and bootstrap gates pass.

## 8. Status After Full Pass

Status after executing the remaining continuity backlog:

1. River and road contracts now reach the true local-map boundaries instead of stopping one tile in.
2. Continuous surface-generation passes now use a shared continuity seed plus global noise origins instead of per-local noise universes.
3. Boundary-adjacent ecology placement now uses continuity-anchored deterministic sampling for seam-relevant tree and plant decisions.
4. Border safety cleanup is now seam-safe. Border repair no longer copies inward local tiles and instead resolves deterministic passable fallback surfaces.
5. Seam diagnostics are centralized in a shared boundary-comparison helper used by tests, analyzer reporting, and the worldgen viewer.
6. Analyzer reporting now records local boundary sample counts plus surface, water, ecology, and tree mismatch ratios.
7. The worldgen viewer now exposes local surface, water, and ecology continuity overlays by comparing the active local map against generated neighbors.
8. Broader worldgen validation was re-run and analyzer thresholds were rebalanced where the old budgets no longer matched the corrected generation behavior.

Validation completed:

1. `dotnet test .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj`
   Passed: `170/170`.
2. `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
   Passed.
3. `DF_SMOKE_FILTER=render-mode-residency` with `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
   Passed. Preview/live chunk residency stayed bounded and the streamed preview path remained queryable and read-only.

Optional follow-up:

1. Use the new local continuity overlays during future manual viewer investigations if a visual seam report resurfaces.
2. Add a more explicit chunk-edge visual smoke only if a concrete renderer artifact remains reproducible after the current worldgen-side fixes.

Longer-term items still deferred from the broader roadmap:

1. Add a true world river edge graph and world-scale hydrology storage.
2. Add explicit region boundary hydrology contracts with discharge/order propagation.
3. Upgrade the worldgen viewer with elevation, flow, river-network, soil-depth, and continuity mismatch overlays.
4. Run broader realism and gameplay sweeps once the parent-to-child hydrology chain is complete.

## 9. Definition of Done

Mapgen is "done" for this pass only when all are true:

1. Rivers are continuous across world->region->local in representative seeds.
2. Local isolated river artifacts are rare and explainable (lakes/sinks), not dominant.
3. Vegetation patterns follow hydrology and terrain.
4. Determinism, continuity, realism, and gameplay test suites are green.
