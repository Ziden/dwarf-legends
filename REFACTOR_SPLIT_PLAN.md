# Refactor Split Plan

## Goal

Reduce delivery risk in the two largest orchestration classes without changing gameplay semantics:

- `src/DwarfFortress.GodotClient/Scripts/GameRoot.cs`
- `src/DwarfFortress.WorldGen/Maps/EmbarkGenerator.cs`

The rule for both is the same: split by responsibility, keep behavior deterministic, and keep public entry points stable while extracting internals.

## GameRoot split

## Current role

`GameRoot` currently owns too many concerns:

- simulation startup and tick flow
- camera and viewport state
- tile cache invalidation
- terrain rendering
- entity rendering
- selection and panel routing
- feedback pulses and overlays
- world FX and emotes

## Target structure

Keep `GameRoot` as the composition root and extract helpers for:

- `GameRenderSurface`
  - tile rendering orchestration
  - ground-transition cache
  - redraw invalidation boundaries
- `SelectionPresentationController`
  - tile / dwarf / item / building panel routing
- `ViewportStateController`
  - zoom, camera, visible-bounds math
- `WorldOverlayRenderer`
  - stockpiles, designations, selection boxes, hazard overlays
- `SimulationLoopController`
  - tick stepping, pause/speed state, profiler forwarding

## Safe extraction order

1. Extract pure helpers with no Node ownership.
2. Move panel-routing code out first.
3. Move overlay rendering second.
4. Move cache and redraw logic third.
5. Leave simulation bootstrap in `GameRoot` until the end.

## EmbarkGenerator split

## Current role

`EmbarkGenerator` is already logically staged but still physically monolithic.

It should become a pipeline of explicit passes matching its existing flow:

- surface shape
- underground structure
- hydrology
- ecology
- hydrology polish
- civilization overlay
- playability
- population

## Target structure

Introduce pass classes with stable inputs/outputs:

- `SurfaceShapePass`
- `UndergroundStructurePass`
- `HydrologyPass`
- `EcologyPass`
- `HydrologyPolishPass`
- `CivilizationOverlayPass`
- `PlayabilityPass`
- `PopulationPass`

Keep `EmbarkGenerator.Generate()` as the façade that wires the passes together.

## Constraints

- Do not change generation order while extracting.
- Do not move state into global singletons.
- Keep `LocalGenerationContext` deterministic and explicit.
- Preserve existing snapshot/diagnostic capture after each stage.

## Shared refactor rules

1. Extract pure functions before stateful services.
2. Keep integration entry points stable until the final cleanup step.
3. Add or keep focused tests around each extraction boundary.
4. Do not mix functional refactors with gameplay changes.

## Recommended order

1. Split `EmbarkGenerator` into pass classes because it already has natural stage boundaries.
2. Split `GameRoot` panel and overlay responsibilities.
3. Split `GameRoot` render/cache responsibilities.
4. Reassess whether further decomposition is still needed after that.

## Progress

- [x] Phase 1: Deleted orphaned CameraController.cs and RenderCache.cs
- [x] Step 1: Extracted RenderCache class (tile cache, ground transitions, entity render positions, visible bounds)
- [x] Step 2: Extracted ViewportController (camera movement, zoom, Z-layer, JumpToTile)
- [ ] Step 3: Extract WorldOverlayRenderer (stockpiles, designations, selection boxes, hover highlights)
- [x] Step 4: Extracted SelectionPanelRouter (DwarfPanel, WorkshopPanel, TileInfoPanel, ItemDetailPanel routing)
- [x] Step 5: Extracted SimulationLoopController (tick stepping, pause/speed, accumulator)
