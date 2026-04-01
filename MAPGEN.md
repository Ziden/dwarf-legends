# MAPGEN Plan: 3-Tier World -> Region -> Local Generation

## 1. Objective

Evolve map generation from a single local embark generator into a deterministic, hierarchical pipeline with three scales:

1. **World Layer (Macro):** high-level world tiles (climate, macro biomes, civilizations, major rivers/mountain chains).
2. **Region Layer (Meso):** each world tile expands to a regional chunk map (landforms, site placement, roads, local hydrology).
3. **Local Layer (Micro):** each regional cell expands to the playable embark map (current `GeneratedEmbarkMap` equivalent with richer context).

This should support deep simulation, streaming, reproducibility, and future world traversal without breaking current fortress startup.

## 1.1 Implementation Status (March 25, 2026)

- [x] Milestone A: Data scaffolding (world/region models + coordinate structs).
- [x] Milestone B: Deterministic world/region prototype + CLI commands (`generate-world`, `generate-region`).
- [x] Milestone C (initial): Local adapter + `MapGenerationService`; fortress startup now routes through layered generation.
- [ ] Milestone D: ContentEditor deep preview tooling (world/region/local inspectors).
- [ ] Milestone E: Persistence + streaming for world/region/local caches.

## 2. Current State (Baseline)

- Local generation exists in `DwarfFortress.WorldGen.Maps.EmbarkGenerator`.
- Game bootstrap currently generates one local map directly via:
  - `DwarfFortress.GameLogic.World.WorldGenerator`
  - `DwarfFortress.GameLogic.Systems.FortressBootstrapSystem`
- Lore/biome selection exists separately in `WorldLoreGenerator`.
- No persistent world/region hierarchy exists yet.

## 3. Design Goals

- **Deterministic:** same world seed + coordinates => same output at every layer.
- **Composable:** higher layers constrain lower layers (biome, river source, geology, faction influence).
- **Streamable:** only load/generate required regions/locals on demand.
- **Incremental migration:** preserve current gameplay path while introducing layers behind adapters.
- **Testable:** strict invariants and seed-based regression tests for each layer.

## 4. Non-Goals (Phase 1)

- Real-time infinite generation.
- Full travel gameplay between multiple local maps.
- Large-scale simulation of every local map tick-by-tick simultaneously.

## 5. Target Data Model

Add three explicit map data models in `DwarfFortress.WorldGen`:

1. `GeneratedWorldMap`
   - Dimensions: `worldWidth x worldHeight`
   - Tile data: macro biome, elevation band, rainfall band, temperature band, tectonic/rock profile id, drainage score, faction pressure.
   - Links: world tile key -> region seed/materialization metadata.

2. `GeneratedRegionMap`
   - Dimensions: `regionWidth x regionHeight` per world tile.
   - Tile data: local biome variant, slope, river/lake flags, vegetation density, resource richness, settlement/road markers.
   - Links: region tile key -> local seed/materialization metadata.

3. `GeneratedLocalMap` (can initially alias existing `GeneratedEmbarkMap`)
   - Current playable tiles + expanded metadata (surface strata, water table hints, vegetation classes).

Coordinate keys:

- `WorldCoord(wx, wy)`
- `RegionCoord(wx, wy, rx, ry)`
- `LocalCoord(wx, wy, rx, ry, lx, ly, z)`

## 6. Deterministic Seed Strategy

Use hierarchical hash derivation:

- `worldSeed = baseSeed`
- `regionSeed = Hash(baseSeed, wx, wy)`
- `localSeed = Hash(baseSeed, wx, wy, rx, ry)`

Rule: no RNG cross-talk between layers. Each generator owns its own `Random` (or deterministic PRNG) from its layer seed.

## 7. Generation Pipeline

## 7.1 World Layer Generation

1. Generate macro elevation/temperature/moisture fields.
2. Derive macro biome classes.
3. Stamp continental features (mountain belts, major rivers, coast style).
4. Attach lore anchors (faction influence envelopes, trade corridors, danger zones).

Outputs:

- `GeneratedWorldMap`
- world-level diagnostics (biome distribution, connected landmass count, river coverage).

## 7.2 Region Layer Generation

For each requested `WorldCoord`:

1. Read parent world tile constraints.
2. Generate region-scale terrain and hydrology consistent with macro inputs.
3. Place sites/roads/resources using lore + macro pressures.
4. Compute embark suitability scores (food, water, danger, ore, wood).

Outputs:

- `GeneratedRegionMap` for `(wx, wy)`
- indexed embark candidates.

## 7.3 Local Layer Generation

For each requested `RegionCoord`:

1. Convert region context into local generator inputs (biome subtype, tree density, outcrop chance, stream count, hostility).
2. Run local tile synthesis (initially adapting current `EmbarkGenerator`).
3. Apply guarantees (passable borders/central area, stair placement, validation budgets).

Outputs:

- playable local map.

## 8. Proposed APIs

Add orchestrator interfaces:

```csharp
public interface IWorldGenerator
{
    GeneratedWorldMap GenerateWorld(int seed, int width, int height);
}

public interface IRegionGenerator
{
    GeneratedRegionMap GenerateRegion(GeneratedWorldMap world, WorldCoord coord);
}

public interface ILocalGenerator
{
    GeneratedEmbarkMap GenerateLocal(
        GeneratedRegionMap region,
        RegionCoord coord,
        LocalGenerationSettings settings);
}
```

Add facade service used by game logic:

```csharp
public interface IMapGenerationService
{
    GeneratedWorldMap GetOrCreateWorld(int seed, WorldGenSettings settings);
    GeneratedRegionMap GetOrCreateRegion(int seed, WorldCoord worldCoord);
    GeneratedEmbarkMap GetOrCreateLocal(int seed, RegionCoord regionCoord, LocalGenerationSettings settings);
}
```

## 9. Storage and Streaming

Phase 1:

- In-memory cache (LRU) for world/region/local outputs.
- Serialize snapshots to JSON/binary in save files.

Phase 2:

- Region/local chunk persistence on disk (`/Saves/<worldId>/regions/...`).
- Background generation queue for prefetching adjacent regions.

## 10. Migration Plan (Safe Rollout)

## Milestone A: Data Scaffolding

- Add new map model types and coordinate structs.
- Keep existing `EmbarkGenerator` unchanged.

## Milestone B: World + Region Prototype

- Implement deterministic world and region generators with minimal fields.
- Add debug CLI commands:
  - `generate-world`
  - `generate-region --wx --wy`

## Milestone C: Local Adapter

- Introduce `LocalGenerationSettings` mapped from region cell metadata.
- Route fortress startup through new `IMapGenerationService`, but with one selected region/local only.
- Preserve existing gameplay outputs as close as possible.

## Milestone D: Tooling and Debug UX

- Extend ContentEditor Map Preview:
  - world tile picker
  - region heatmap view
  - local preview for selected region cell
  - quick stats (trees/water/walls/ore by seed).

## Milestone E: Persistence + Streaming

- Cache invalidation/versioning for generator schema changes.
- Save/load world + visited regions + visited locals.

## 11. Test Strategy

Unit tests:

- Seed determinism per layer.
- Parent-child consistency (macro biome must constrain region/local biomes).
- Invariant checks (passability guarantees, minimum resource thresholds where required).

Property tests (sample many seeds):

- Distribution sanity (biome spread, water ratio, tree ratio by biome).
- Connectivity and border guarantees for locals.

Integration tests:

- `FortressBootstrapSystem` still starts successfully through new service.
- Save/load reproduces exact generated outputs by seed + coordinates.

Regression snapshots:

- Store hashed summaries of representative seeds to detect accidental generation drift.

## 12. Performance Budgets

Initial targets (adjust with profiling):

- World generation: <= 200 ms for default world size.
- Region generation: <= 50 ms per region.
- Local generation: <= 25 ms per local map (current embark-sized).
- Memory: bounded caches with configurable limits.

## 13. File/Module Impact (Planned)

New (expected):

- `src/DwarfFortress.WorldGen/World/*`
- `src/DwarfFortress.WorldGen/Regions/*`
- `src/DwarfFortress.WorldGen/Local/*` (or keep `Maps/*` and refactor gradually)
- `src/DwarfFortress.GameLogic/World/MapGenerationService.cs`

Updated:

- `src/DwarfFortress.GameLogic/World/WorldGenerator.cs` (becomes adapter/facade call)
- `src/DwarfFortress.GameLogic/Systems/FortressBootstrapSystem.cs`
- `src/DwarfFortress.WorldGen.Cli/Program.cs`
- `src/DwarfFortress.ContentEditor/Components/Pages/MapPreview.razor`

## 14. Risks and Mitigations

- **Risk:** generation complexity explosion.
  - Mitigation: strict layer contracts and incremental milestones.
- **Risk:** nondeterministic behavior from shared RNG usage.
  - Mitigation: per-layer/per-coordinate seeded RNG only.
- **Risk:** save compatibility churn.
  - Mitigation: generator version field + migration adapter.
- **Risk:** local gameplay regressions.
  - Mitigation: keep current local generator as baseline adapter until parity tests pass.

## 15. Open Decisions

1. Default world size and world-to-region scale factors.
2. Region-to-local scale factors and whether multiple locals can be stitched seamlessly.
3. How much lore simulation runs globally vs lazily per explored region.
4. Whether to store full generated tiles or store seeds + deltas only.

## 16. Immediate Next Step

Implement the first **Milestone D** debug UX slice:

1. Add `MapPreview` controls for seed + regenerate + coordinate selection.
2. Visualize world tile + region heatmap + selected local summary stats.
3. Add quick diagnostics focused on trees/water/walls/ore counts per regeneration.
