# Chunk Streaming Finish Plan

## Goal

Finish the chunk-streaming work from the current preview-only milestone through production-ready runtime behavior:

- streamed terrain renders correctly beyond the startup local
- preview, remembered, resident, and active chunk states are visually distinct and query-safe
- simulation activates and deactivates chunks without breaking jobs, entities, or determinism
- performance remains bounded while moving the camera and traversing chunk boundaries
- renderer regressions introduced by streaming, including intermittent white floor tiles, are fixed and covered by tests

## Current State

- Visible progress exists: viewport demand publication, chunk activation demand tracking, preview chunk generation, preview rendering, preview hover/query fallback, and read-only preview UI are in place.
- The runtime is still preview-first, not residency-first: `WorldMap` remains a finite live map, and off-local chunks are generated as render/query snapshots rather than full simulation chunks.
- Two blockers now need to be handled before more scope is added:
  - camera movement after preview streaming can lag
  - floor rendering sometimes falls back to white, which indicates a render/material/tile-data mismatch in the new preview path

## Exit Criteria

This work is done when all of the following are true:

1. Crossing local boundaries keeps terrain visible, visually coherent, and bounded in memory.
2. Chunk states are explicit: preview, remembered, resident, active.
3. Only active chunks run full simulation, but resident/offloaded chunks retain enough state to reactivate deterministically.
4. Jobs, movement, item hauling, spatial queries, and announcements remain correct when entities cross chunk boundaries.
5. Fog-of-war and remembered terrain work across streamed chunks.
6. Profiling shows no major frame spikes from chunk preview generation or chunk mesh rebuild churn during normal camera movement.
7. The white-floor regression has a root-cause fix with targeted regression coverage.
8. Focused tests, client build, and smoke coverage exist for the full flow.

## Workstreams

### 1. Stabilize The Current Preview Slice

Purpose: fix the visible regressions before layering more architecture on top.

- Add a dedicated visual treatment for preview chunks so they never look identical to live terrain.
- Fix the intermittent white-floor rendering bug by tracing the full render-data path for streamed chunks:
  - `ChunkPreviewStreamingService`
  - `WorldChunkRenderSnapshot`
  - `WorldChunkSliceMesher`
  - `WorldRender3D`
  - `TileRenderHelper` and material/texture lookup code
- Verify that live and preview floor tiles resolve the same tile/material/ground overlay inputs unless they are intentionally dimmed.
- Add a smoke or targeted render regression that fails when floor tiles render without a valid texture layer.
- Keep preview chunks strictly read-only until active residency is implemented.

Definition of done:

- preview chunks are visibly dimmed or otherwise distinguished
- no white fallback floor tiles in the targeted smoke path
- no new client compile or smoke regressions

### 2. Performance Pass For Streaming And Rendering

Purpose: remove the lag that appeared after preview streaming before expanding residency scope.

- Profile the hot path while moving the camera across local boundaries.
- Measure at minimum:
  - `render3d_sync_slice`
  - `active_chunks`
  - `sync_chunk_meshes`
  - preview snapshot generation in `ChunkPreviewStreamingService`
  - mesh rebuild churn in `WorldRender3D`
- Confirm whether lag comes mainly from:
  - repeated adjacent-local generation
  - chunk snapshot recapture churn
  - chunk mesh rebuild churn
  - tile render data/material lookup churn
  - excessive preview residency size
- Apply bounded fixes at the root cause, likely in one or more of these areas:
  - cache invalidation policy for preview chunk snapshots
  - chunk build queue throttling and prioritization
  - avoiding rebuilds when only hover/query state changes
  - tighter resident-ring sizing or asymmetric prefetch
  - reusing shared tile render data instead of recomputing it per tile where possible
- Add profiler monitors or focused tests for the chosen seam so performance regressions are visible earlier.

Definition of done:

- normal camera pan/jump across chunk boundaries no longer produces obvious lag spikes in gameplay
- chunk residency stays bounded
- the chosen hot path has instrumentation and at least one repeatable validation path

### 3. Formalize Chunk State Model

Purpose: move from ad hoc preview behavior to an explicit runtime model.

- Define chunk lifecycle states and their contracts:
  - preview: generated for render/query only, read-only
  - remembered: last-known terrain under fog, no live entities unless separately represented
  - resident: runtime chunk data is loaded and can be promoted quickly
  - active: full simulation tick participation
- Make the state model live in GameLogic, not only in client rendering code.
- Add shared metadata for:
  - origin and owning world/region/local coordinate
  - current residency state
  - dirty/version info
  - last-seen or last-active tick
  - eligibility for eviction or promotion
- Ensure `ChunkActivationManager` becomes the authoritative demand model for active vs resident chunk targets.

Definition of done:

- every streamed chunk has an explicit lifecycle state and transition rules
- client and query code consume the same state model instead of inventing local heuristics

### 4. Introduce Sparse Runtime Residency In GameLogic

Purpose: stop relying on one permanently materialized embark-local `WorldMap`.

- Refactor the world runtime so live chunk storage is sparse rather than tied to a fixed rectangular local allocation.
- Separate chunk lookup from a single finite width/height assumption in systems that still assume `WorldMap` is the whole world.
- Keep active chunk storage authoritative for simulation while allowing non-active resident chunks to stay loaded but unticked.
- Preserve deterministic tile generation by continuing to source missing terrain from the layered worldgen pipeline.
- Define how chunk data is materialized, retained, and evicted without leaking Godot concepts into GameLogic.

Definition of done:

- runtime can hold an arbitrary resident set of chunks around the player instead of only one generated local
- core systems can query resident chunks without assuming a finite embark rectangle

### 5. Promote Resident Chunks Into Active Simulation

Purpose: replace read-only preview behavior with real streamed gameplay.

- Add activation and deactivation flows for chunks entering or leaving the active ring.
- Materialize live chunk state when a resident chunk becomes active.
- Ensure chunk promotion populates:
  - tiles
  - items
  - buildings
  - stockpiles
  - entities and their spatial index presence
  - relevant per-chunk runtime systems or registries
- Define what remains loaded when a chunk is resident but inactive.
- Ensure re-entry does not duplicate objects or lose ownership/state.

Definition of done:

- entering a new local boundary can promote real gameplay chunks, not just preview terrain
- moving back and forth across chunk boundaries does not duplicate or erase runtime state

### 6. Offload, Catch Up, And Preserve Determinism

Purpose: make inactive chunks cheap without breaking world continuity.

- Define the offload format for dormant chunks.
- Decide which systems need catch-up simulation and which can be recomputed lazily.
- Add chunk reactivation catch-up for time-sensitive systems such as:
  - needs
  - vegetation growth or harvestable state
  - fluids if supported in inactive space
  - job validity and path assumptions
  - announcements or delayed consequences that must still surface
- Ensure entities displaced by activation boundaries maintain correct movement/spatial/event invariants.
- Guarantee that chunk deactivation/reactivation is deterministic for a given seed plus saved runtime state.

Definition of done:

- inactive chunks stop consuming full tick cost
- reactivated chunks reflect elapsed time without obvious simulation discontinuities

### 7. Finish Query, Input, And UI Semantics

Purpose: complete the player-facing rules after active residency exists.

- Extend `WorldQuerySystem` and query models so chunk-state semantics are explicit everywhere.
- Support the full set of query behaviors:
  - active chunks: full interaction
  - resident inactive chunks: limited or queued interaction if allowed by design
  - remembered chunks: inspect remembered terrain only
  - preview chunks: render/query only, read-only
- Review input/designation/build menu/resource UI for chunk-state aware affordances.
- Prevent player actions that would require simulation state the current chunk does not have.
- Ensure hover, inspection, announcements, and tile-target UI stay consistent across chunk states.

Definition of done:

- player affordances match chunk state consistently across 2D/3D views and inspector UI
- no hidden “looks selectable but does nothing” interactions remain

### 8. Finish Fog-Of-War And Remembered Terrain

Purpose: complete the visibility model promised by the original plan.

- Add remembered chunk/tile storage and visual presentation.
- Distinguish remembered terrain from preview terrain both visually and in query data.
- Ensure remembered data comes from actual explored live state, not fresh worldgen snapshots.
- Decide what non-terrain information persists in memory:
  - buildings
  - stockpiles
  - items
  - creatures or only last-known traces
- Make fog transitions stable when moving the camera between active and remembered areas.

Definition of done:

- explored-but-inactive terrain appears as remembered state rather than live or preview state
- remembered data never leaks new information the player has not discovered

### 9. Harden Cross-System Simulation Boundaries

Purpose: remove the hidden single-local assumptions that will break once full streaming is live.

- Audit systems that implicitly assume one finite map, especially:
  - pathing and job step consumption
  - hauling and reservation lookups
  - building placement and stockpile coverage
  - entity movement and spatial indexing
  - announcement and recent-activity systems
  - any full-map scans in tick paths
- Replace broad scans with tracked sets or chunk-aware lookups where needed.
- Ensure direct movement still emits `EntityMovedEvent` and keeps spatial/query systems synchronized across chunk boundaries.
- Add tests for chunk-boundary path and job invalidation scenarios.

Definition of done:

- chunk streaming no longer depends on fragile single-map assumptions in unrelated systems
- hot-path systems avoid new full-world rescans

### 10. Validation, Soak, And Release Criteria

Purpose: close the feature with repeatable confidence instead of one-off manual verification.

- Keep focused unit tests for each streaming seam in `DwarfFortress.Tests`.
- Add smoke coverage for:
  - camera travel across chunk boundaries
  - preview vs active visual distinction
  - no white-floor regression
  - bounded chunk residency after long camera movement
  - query and input behavior in each chunk state
- Run broader validation before calling the feature done:
  - relevant focused test filters
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
  - headless Godot smoke suite for chunk streaming paths
  - broader solution build if the touched scope warrants it
- Update architecture and implementation docs to reflect the final runtime model.
- Remove temporary preview-only scaffolding once superseded by real residency flow.

Definition of done:

- the feature has targeted tests, smoke validation, and updated docs
- remaining known issues are explicit, small, and outside the chunk-streaming scope

## Recommended Execution Order

1. Stabilize preview visuals and fix the white-floor regression.
2. Run the streaming performance pass and eliminate the current lag.
3. Formalize chunk lifecycle state in GameLogic.
4. Introduce sparse resident runtime storage.
5. Promote resident chunks into active simulation.
6. Add offload and catch-up rules for inactive chunks.
7. Finish query/input/UI semantics for all chunk states.
8. Finish fog-of-war and remembered terrain.
9. Harden cross-system chunk-boundary assumptions.
10. Run final validation, soak, and cleanup.

## Immediate Next Step

Start with the stabilization slice, not more architecture. The current best next milestone is:

1. add preview dimming or another explicit preview visual treatment
2. root-cause the white-floor floor-material regression in the preview render path
3. profile chunk movement to identify whether the lag is mesh churn, preview generation churn, or render-data churn
