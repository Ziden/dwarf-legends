# Vegetation + Relief Re-Plan

## Why We Missed It

1. Embark tree generation is count-based, not coverage-based.
`EmbarkGenerator` uses fixed per-biome ranges like `16..28` trees for a `48x48` map (2304 cells), which is too sparse for "real forest" visuals.

2. Forest tests are relative, not absolute.
Current tests mainly verify "forest > steppe" but do not enforce minimum canopy coverage targets.

3. We erase large playable zones after generation.
`EnsureCentralEmbarkZone` removes all features in a 16x16 center block, which can erase dense patches and make maps feel flat.

4. World visualization hides biome differences.
`PixelArtFactory.GetWorldTile` maps most biomes to a single default green tile, so forests and mountains are not visually obvious even when data exists.

5. Region/local vegetation does not enforce patch continuity.
Noise influences density, but there is no explicit canopy patch model (core, edge, gap), so forest blobs are weak.

## Goals

1. Embark maps show clear forest character when biome/context says forest.
2. World view clearly communicates mountain/forest gradients (none/half/full or equivalent).
3. Vegetation looks continuous and ecological, not random scattered trees.
4. Keep deterministic generation and safe embark playability.

## Phase 1: Add Missing Signals (World + Region)

### World tile metadata

Add explicit fields to world tiles:
- `ForestCover` (0..1)
- `Relief` (0..1)
- `MountainCover` (0..1)

Derive from existing signals:
- `ForestCover`: moisture + runoff + river influence - elevation/slope penalties.
- `Relief`: elevation + ridges.
- `MountainCover`: high relief with high elevation.

Create display buckets:
- `None` = < 0.33
- `Half` = 0.33..0.66
- `Full` = > 0.66

### Region inheritance

Region generation must consume world `ForestCover`/`MountainCover` directly:
- raise/lower `VegetationDensity` from world canopy budget.
- raise outcrop/stone tendencies from world relief budget.

## Phase 2: Switch Embark Trees to Coverage Targets

Replace fixed tree counts with target canopy ratio:
- Example target ratios (before local modifiers):
  - TropicalRainforest: 0.45..0.70
  - Conifer/BorealForest: 0.30..0.55
  - TemperatePlains: 0.08..0.22
  - Savanna/Steppe: 0.02..0.12
  - Desert/Ice: 0.00..0.03

Pipeline:
1. Compute target tree tile count from ratio and map area.
2. Build canopy suitability map (moisture + soil + riparian + canopy noise - ruggedness penalty).
3. Seed forest cores in top suitability cells.
4. Grow outward with decay to create contiguous patches.
5. Fill until target reached.

Keep deterministic seeds for all stochastic steps.

## Phase 3: Preserve Playability Without Flattening Ecology

Replace hard clear with constrained spawn-safe policy:
- Keep a guaranteed passable embark zone, but do not force full wipe.
- Only remove blocking tiles needed for guaranteed start paths.
- Optional: reduce clear zone from 16x16 to 10x10 and preserve some natural tiles.

## Phase 4: World Viewer Visual Fix

### Data view improvements

In world viewer:
- Add overlays:
  - `Forest Cover`
  - `Relief`
  - `Mountain Cover`
- Continue elevation/river overlays.

### Tile visual language

Update world tile rendering so all major biomes are distinct.

Add forest/mountain glyph overlays by bucket:
- Forest:
  - half: sparse canopy marker
  - full: dense canopy marker
- Mountain:
  - half: foothill marker
  - full: peak marker

Do not rely on one default green for non-desert/non-ocean biomes.

## Phase 5: Tests and Budgets (Required)

### Embark vegetation tests

Add absolute canopy assertions on representative seeds:
- Conifer/Boreal forest: `>= 20%` tree coverage median.
- Tropical rainforest: `>= 35%` tree coverage median.
- Steppe/Desert: capped upper bounds.

Add continuity metric:
- Largest tree component ratio above threshold in forest biomes.

### World/region signal tests

Add tests for:
- `ForestCover` bounded [0,1]
- `MountainCover` correlated with elevation/ridges
- Region vegetation positively correlated with parent `ForestCover`

### Visual mapping guardrails

Add tests for known biome IDs:
- no fallback/default texture path for known macro biomes.
- forest/mountain bucket overlays produced for expected cases.

## Implementation Order

1. Add world/region canopy-relief fields and tests.
2. Refactor embark trees to coverage + patch growth.
3. Replace hard clear with spawn-safe constrained clearing.
4. Upgrade world viewer overlays + biome rendering mapping.
5. Tune thresholds against seed sweeps and lock budgets in tests.

## Definition of Done

1. Forest biomes visibly produce dense forests on embark maps in normal seeds.
2. World view clearly shows mountain + forest gradients.
3. Forest continuity metrics pass on seed sweeps.
4. Passability and deterministic behavior remain stable.
5. New tests prevent regressions in density, continuity, and rendering.
