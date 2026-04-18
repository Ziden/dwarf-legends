# Worldgen

This document describes how world generation currently works in the codebase.

Use this as the implementation guide for agent work.

Use `MAPGEN.md` and `DF_WORLD_GEN.md` for broader roadmap context. This file is about the code that exists now.

## Purpose

Worldgen is a deterministic, layered pipeline that builds the playable embark map from broader parent scales.

The main goals are:

- keep each layer reproducible from seed and coordinates
- let higher layers constrain lower layers instead of letting each layer improvise independently
- keep runtime orchestration in GameLogic and generation logic in `DwarfFortress.WorldGen`
- preserve continuity across shared boundaries, especially rivers, roads, ecology, and surface families

## Project Boundaries

World generation code lives primarily in `src/DwarfFortress.WorldGen`.

Runtime orchestration and caching live in `src/DwarfFortress.GameLogic/World/MapGenerationService.cs`.

Important rule: generation code is pure worldgen logic. GameLogic owns cache lifetime, startup orchestration, and application into `WorldMap`.

## Layered Pipeline

The implemented pipeline is:

1. `WorldLayerGenerator`
   - File: `src/DwarfFortress.WorldGen/World/WorldLayerGenerator.cs`
   - Produces `GeneratedWorldMap`
   - Builds macro scalar fields such as elevation, temperature, moisture, drainage, relief, mountain cover, forest cover, rivers, and optional roads

2. `HistorySimulator`
   - File: `src/DwarfFortress.WorldGen/History/HistorySimulator.cs`
   - Produces `GeneratedWorldHistory`
   - Expands the world map into civilizations, territories, sites, roads, households, figures, yearly snapshots, and historical events

3. `RegionLayerGenerator`
   - File: `src/DwarfFortress.WorldGen/Regions/RegionLayerGenerator.cs`
   - Produces `GeneratedRegionMap`
   - Expands one world tile into region cells with biome variants, slope, hydrology, vegetation metrics, roads, settlements, and boundary contracts

4. `LocalLayerGenerator`
   - File: `src/DwarfFortress.WorldGen/Local/LocalLayerGenerator.cs`
   - Adapts one region cell into resolved `LocalGenerationSettings`, continuity contracts, and field maps

5. `EmbarkGenerator`
   - File: `src/DwarfFortress.WorldGen/Maps/EmbarkGenerator.cs`
   - Produces the final `GeneratedEmbarkMap`
   - Builds the playable local map from the resolved local settings and region-derived field maps

At runtime, `MapGenerationService` composes the full flow:

- world cache
- history cache
- region cache
- local cache
- local map selection and application into `WorldMap`

## Core Types

These are the main types to understand before changing anything:

- `GeneratedWorldMap`
  - macro-scale output

- `GeneratedRegionMap`
  - per-world-tile region expansion

- `GeneratedEmbarkMap`
  - final local playable map

- `WorldCoord`
  - coordinate of a world tile

- `RegionCoord`
  - coordinate of a region tile under a world tile

- `LocalGenerationSettings`
  - input contract for `EmbarkGenerator`

- `LocalContinuityContract`
  - continuity-focused subset of local settings that must remain stable across adjacent locals

- `LocalGenerationFingerprint`
  - stable hash for local cache identity

- `LocalRegionFieldMaps`
  - interpolated region-scale scalar fields sampled into local resolution

## Determinism Rules

Determinism is non-negotiable.

Rules to preserve:

- Seed derivation goes through `SeedHash`.
- Layer outputs depend only on seed, settings, coordinates, parent data, and content.
- Do not share mutable RNG state across layers.
- Cache identity must include every setting that changes generated output.
- If you add new local-generation inputs, update `LocalGenerationFingerprint` and any cache keys that depend on it.

`MapGenerationService.LocalCacheKey` already includes the local settings fingerprint. That is the main identity seam for generated locals.

## World Layer

`WorldLayerGenerator` creates the broad world fields.

Current responsibilities:

- generate macro terrain scalars
- derive macro biome IDs
- solve world hydrology using `HydrologySolver`
- mark river masks and river edges
- derive relief, mountain cover, and forest cover
- optionally derive road masks when the feature flag is enabled

The world layer is the source of truth for:

- macro biome identity
- region parent constraints
- macro river presence and discharge
- broad forest and mountain character

## History Layer

`HistorySimulator` turns a world map into a world history timeline.

Current responsibilities:

- create civilizations
- assign territory
- place sites
- build roads between sites
- simulate yearly events and population changes
- generate households and historical figures

This layer feeds continuity-sensitive downstream systems:

- region road overlays
- region settlement overlays
- local history context

Site and event IDs should come from shared constants in `WorldGenIds.cs`, not raw strings in logic code.

## Region Layer

`RegionLayerGenerator` expands a world tile into a denser regional map.

Key ideas:

- It blends edge-projected sampling with interior-focused sampling so adjacent world tiles align at borders without erasing the parent tile's center identity.
- It computes region-scale flow accumulation and hydrology.
- It stamps river boundary contracts and road boundary contracts that downstream local generation can reuse.
- It overlays history-driven settlements and roads when history is enabled.

Important continuity behavior:

- river edges are not just booleans; they carry edge masks and discharge/order context
- road edges work the same way
- local generation should prefer these boundary contracts over ad hoc neighbor guesses

## Local Layer

`LocalLayerGenerator` is the adapter between region output and `EmbarkGenerator` input.

Current responsibilities:

- resolve the effective local biome ID
- translate region metrics into tree, wetness, soil, and outcrop biases
- derive stream and marsh biases from region hydrology
- incorporate history-driven settlement and road influence
- build river portals, road portals, settlement anchors, and continuity contracts
- build `LocalRegionFieldMaps` by sampling nearby region tiles into local resolution

This layer is where most continuity-sensitive local tuning lives.

### Local Continuity Inputs

The local layer feeds continuity through these mechanisms:

- `LocalContinuityContract`
- `LocalRiverPortal[]`
- `LocalRoadPortal[]`
- `LocalSettlementAnchor[]`
- `NoiseOriginX` and `NoiseOriginY`
- `LocalRegionFieldMaps`

Boundary offsets for river, settlement, and road seams must use canonical shared-edge keys. The local layer now resolves these through one shared canonical-edge helper so the three systems stay in lockstep.

## Embark Generator

`EmbarkGenerator` consumes `LocalGenerationSettings` plus optional region field maps.

Current responsibilities include:

- surface terrain synthesis
- hydrology carving
- vegetation and ecology placement
- geology, caves, magma, and wildlife integration
- playability and population passes
- stage diagnostics snapshots

When local behavior changes, this is usually where the actual tile output changes. When parent influence changes, `LocalLayerGenerator` is usually the right place.

## Content-Driven Worldgen

The content catalog is the main data-driven seam.

Important files:

- `src/DwarfFortress.WorldGen/Config/WorldGenContentCatalog.cs`
- `data/ConfigBundle/worldgen/worldgen_content.json`

The catalog currently owns:

- geology profiles and mineral veins
- tree biome profiles
- biome generation profiles
- surface and cave wildlife
- history figure generation hooks

If a change can be expressed in content, prefer content over hardcoded branching.

## Shared IDs and Semantics

Shared worldgen IDs live in `src/DwarfFortress.WorldGen/Ids/WorldGenIds.cs`.

Use these for logic instead of raw string literals:

- `MacroBiomeIds`
- `RegionBiomeVariantIds`
- `RegionSurfaceClassIds`
- `HistoricalEventTypeIds`
- `SiteKindIds`

`SiteKindIds` also exposes semantic helpers for substring-based matching, because history/site config still supports custom names such as `watchtower` or `ridge_hamlet`.

## Runtime Orchestration

`MapGenerationService` is the runtime facade.

Important responsibilities:

- cache world, history, region, and local outputs
- compute local cache keys from `LocalGenerationFingerprint`
- select the default embark coordinate
- apply generated local maps into `WorldMap`
- expose the last generated world, history, region, and local context to runtime systems

Do not bypass this service for fortress startup or preview generation unless the task explicitly needs raw generator access.

## Validation

Default validation commands:

```powershell
dotnet build .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj
dotnet test .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj
dotnet test .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj --filter LocalLayerGenerator
dotnet test .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj --filter WorldGenAnalyzer
dotnet test .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj --filter HistorySimulator
```

Key regression surfaces:

- `LocalLayerGeneratorTests`
- `EmbarkGeneratorTests`
- `RegionLayerGeneratorTests`
- `WorldLayerGeneratorTests`
- `WorldGenAnalyzerTests`
- `HistorySimulatorTests`

When changing continuity behavior, always prefer the narrowest relevant test slice first.

## Common Change Patterns

### Add or adjust a macro biome

Update:

- `MacroBiomeIds`
- world-layer biome classification
- region biome variant mapping in `RegionBiomeVariantIds`
- content catalog biome profiles
- tests that assert biome coverage or density expectations

### Change river behavior

Check all of these layers:

- world hydrology and river masks
- region river masks, edge contracts, and discharge
- local river portals and continuity contracts
- embark hydrology carving
- continuity tests in worldgen tests

If you only change one layer, you can easily create a seam instead of a fix.

### Change local ecology or forest behavior

Check:

- `LocalLayerGenerator` bias resolution
- `LocalRegionFieldMaps` sampling
- `EmbarkGenerator` ecology and vegetation passes
- dense forest and continuity tests

### Change history-driven settlements or roads

Check:

- `HistorySimulator`
- region history overlays
- local history context
- local settlement anchors and road portals

## Cleanup Pass: Tech Debt Addressed

This cleanup pass normalized several concrete worldgen smells.

1. Region biome metadata was scattered.
   - Fixed by centralizing macro-biome resolution and tree-density bias lookup in `RegionBiomeVariantIds`.

2. Lore event types and site kind semantics used raw strings in logic.
   - Fixed by adding shared `HistoricalEventTypeIds` and `SiteKindIds` helpers.

3. `LocalLayerGenerator` built the same resolved continuity values twice.
   - Fixed by introducing a single resolved local embark contract object and applying it once to both settings and continuity contract.

4. River, settlement, and road boundary offsets duplicated canonical-edge logic.
   - Fixed by routing them through one canonical edge key resolver.

5. Neighborhood vegetation and suitability sampling duplicated the same weighted 3x3 loop.
   - Fixed by consolidating the sampling into one generic neighborhood scalar helper.

## Watchpoints

These are the main areas that still deserve caution.

1. `WorldLayerGenerator.Generate` is still a large orchestration method.
   - The behavior is stable, but future work may want stage extraction rather than more inline growth.

2. `RegionLayerGenerator.Generate` is also a large orchestration hub.
   - Keep new behavior in helpers where possible.

3. Site-kind matching intentionally remains partly fragment-based.
   - That is not ideal from a pure typing perspective, but it preserves config extensibility for custom site IDs.

4. `WorldGenFeatureFlags.EnableRoadGeneration` is still a global mutable feature flag.
   - Treat it carefully in tests and do not assume it is always enabled.

## Agent Checklist

Before changing worldgen code:

1. Identify which layer owns the behavior.
2. Confirm whether the change belongs in data, world, region, local, or runtime orchestration.
3. Check whether cache identity or continuity contracts need to change.
4. Prefer shared IDs and helper methods over new raw string checks.
5. Run the narrowest relevant tests before broader validation.

If a bug is visible at chunk or embark boundaries, assume it may involve more than one layer until proven otherwise.