# Rendering Guide

## Purpose

This folder contains the Godot-side rendering pipeline for the playable client.

`DwarfFortress.GameLogic` remains the source of truth for world state and gameplay rules.
The code here reads live systems, chunk snapshots, and query DTOs, then turns that data into:

- chunk terrain meshes
- structure meshes
- actor and item billboards
- vegetation billboards
- world overlays and feedback
- hover/picking helpers used by the client and smoke tests

Treat rendering caches as presentation caches only. Do not move gameplay state into them.

## Current Frame Flow

The active 3D path is split into two major sync phases:

1. `WorldRender3D.SyncSlice(...)`
   Handles slice-scoped world data:
   - active chunk collection
   - chunk snapshot refresh
   - chunk mesh queueing and building
   - terrain texture-array refresh
   - structure mesh sync
   - designation and stockpile overlays

2. `WorldRender3D.SyncDynamicState(...)`
   Handles camera- and entity-driven presentation:
   - dynamic overlays
   - vegetation billboards
   - actor and item billboards
   - movement interpolation presentation
   - water effects
   - world-space labels and lightweight FX

That split matters:

- `SyncSlice(...)` is the place for slice/terrain/structure residency work.
- `SyncDynamicState(...)` is the place for camera-facing billboards and dynamic per-frame presentation work.

Do not casually move heavy slice work into `SyncDynamicState(...)`.

## Main Ownership Boundaries

### `WorldRender3D`

`WorldRender3D` is the top-level 3D world orchestrator.
It owns:

- active chunk collection and residency
- chunk snapshots
- chunk build queueing
- terrain mesh attachment
- structure mesh orchestration
- overlay mesh orchestration
- actor presentation orchestration
- vegetation presentation orchestration
- billboard picking routing
- debug hooks used by smoke tests

It should not become the place where feature-specific rendering logic lives.
If a feature needs stateful rendering behavior, prefer a focused helper or renderer owned by `WorldRender3D`.

### `WorldActorPresentation3D`

Owns actor/item-like billboard presentation:

- dwarves
- creatures
- loose items
- carried items
- item preview stacks
- waterline/water tint presentation for actors/items
- world FX labels and emote bubbles
- actor/item billboard picking

This renderer is currently node-based, not `MultiMesh`-batched.
That is fine for now because actors/items need more per-entity presentation state than vegetation.

### `VegetationInstanceRenderer`

Owns vegetation billboard presentation:

- trees
- ground plants
- shared `MultiMesh` batching
- vegetation material creation
- vegetation picking
- vegetation debug probes

Important current contract:

- the main tree silhouette is the base texture
- optional canopy/fruit detail is a separate overlay texture on the same geometry
- the active 3D path should not use baked composite tree textures

### Terrain Pipeline

Terrain is custom and chunk-based.
The important pieces are:

- `WorldChunkRenderSnapshot`
  Chunk snapshot for rendering.
- `WorldChunkSliceRenderCache`
  Per-slice cache for `TileRenderData` and resolved display ground.
- `WorldChunkSliceMesher`
  Builds the terrain mesh surfaces for a slice.
- `TerrainSurfaceRecipeBuilder`
  Produces terrain surface recipes and transitions.
- `TerrainSurfaceArrayLibrary`
  Shared 3D texture-array storage for terrain surfaces.
- `TerrainSurfaceCanvasCache`
  Standalone `Texture2D` cache for 2D/canvas consumers that truly need a texture instead of a layer index.
- `Terrain/*`
  Ground resolution, smoothing, transitions, liquid patches, and ore overlays.

Do not move this terrain path into Godot `TileMap` or `GridMap`.
The renderer depends on:

- warped top surfaces
- exposed side faces
- fluid height differences
- custom transition blending
- ore/detail overlays
- preview tinting
- streamed chunk slices

### Visual Asset Helpers

The main visual asset entry points are:

- `WorldSpriteVisuals`
  High-level world-facing sprite helpers and sprite sizes.
- `Visuals/PixelArtFactory`
  Generated textures and pixel-art composition.
- `Visuals/DwarfSpriteComposer`
  Dwarf-specific sprite composition.
- `Visuals/CreatureSpriteComposer`
  Creature-specific sprite composition.

`WorldSpriteVisuals` should stay as the main caller-facing helper.
Avoid making gameplay/UI code reach deep into `PixelArtFactory` unless the code is truly about low-level texture generation.

## Folder Map

### `Terrain/`

Terrain-specific helpers:

- smoothing profiles
- terrain transition resolution
- ground material resolution
- display-ground logic
- liquid transition patch rendering
- ore/detail overlays
- shared tile smoothing helpers

This is where terrain blending rules should live.
Do not reintroduce one-off terrain neighbor checks in unrelated renderers.

### `Visuals/`

Low-level sprite generation and sprite registries.

This is mostly texture creation, not world orchestration.

### Root rendering files

Important root-level files:

- `WorldRender3D.cs`
- `WorldActorPresentation3D.cs`
- `VegetationInstanceRenderer.cs`
- `WorldChunkSliceMesher.cs`
- `WorldChunkSliceRenderCache.cs`
- `WorldHoverHighlightRenderer3D.cs`
- `WorldStructureMesher.cs`
- `WorldOverlayMesher.cs`
- `WorldSliceHoverResolver3D.cs`
- `TerrainRenderStats.cs`

## Rendering Contracts That Matter

### 1. Runtime source of truth

Use live systems whenever the client needs current runtime truth.

Important rule:

- non-preview tiles should prefer live `WorldMap` state over stale snapshot assumptions when the current renderer path needs up-to-date tile data

This matters for designation state, cleared ground, and vegetation presentation.

### 2. Chunk snapshots are render inputs, not gameplay truth

Chunk snapshots exist so terrain/structure rendering can work with stable slice data.
They are not the canonical gameplay state.

### 3. Terrain textures are shared, not per-chunk atlases

Terrain already uses shared texture arrays through `TerrainSurfaceArrayLibrary`.
Do not regress back to one standalone texture per terrain-surface recipe in the 3D path.

### 4. Tree rendering is an overlay over inferred/display ground

A tree tile is not a fully opaque replacement for the ground below it.
The terrain under the tree still matters for:

- display ground
- terrain blending
- smoothing with neighbors
- what the tile should look like if the tree is chopped

If a tree blend bug appears, inspect display-ground resolution and transition inputs first.

### 5. Hover emphasis has dedicated ownership

Hovered semantic targets now render through `WorldHoverHighlightRenderer3D`.

Current ownership split:

- `WorldHoverHighlightRenderer3D` owns hover-only emphasis visuals
- `WorldOverlayMesher` still owns the generic raw-tile hover plate fallback
- `WorldActorPresentation3D` owns normal actor/item billboards
- `VegetationInstanceRenderer` owns normal vegetation billboards

Important rules:

- do not add hover state directly into `VegetationInstanceRenderer` batch keys or `MultiMesh` data
- do not tint or mutate shared billboard materials cached by `WorldActorPresentation3D`
- do not move hover target resolution into renderer-local caches; keep it driven by live query data through `HoverSelectionResolver`
- if a hovered target exists, the semantic hover renderer owns the emphasis and the generic raw-tile plate should not stack on top of it

### 6. Picking order is intentional

`WorldRender3D` routes billboard picking in this order:

1. actor/item billboards
2. vegetation billboards

Vegetation billboard hits resolve as raw tile selection, not actor/item selection.
That ordering is important for interaction behavior and smoke tests.

## Current Material / Render Priority Conventions

These values are implementation details, but they are useful to know when debugging overlap:

- overlays in `WorldRender3D`: render priority `0`
- main billboards: render priority `1`
- vegetation canopy/detail overlay pass: render priority `2`
- carried items: render priority `3`
- hover ring/plate: render priority `4`
- hover billboard overlays: render priority `5-6`
- emote bubbles/icons: render priority `10+`

The current vegetation path uses cutout-style rendering, not translucent alpha blending:

- base vegetation material: `AlphaScissor`
- canopy/detail overlay material: `AlphaScissor`

This was chosen to avoid the depth instability of overlapping transparent tree billboards.

## Current Quirks And Debt To Be Aware Of

These are not all bugs, but they are important seams for future agents.

### Vegetation picking contract is not fully cleaned up yet

`VegetationInstanceRenderer` currently renders vegetation with `AlphaScissorThreshold = 0.5f`, but hit-testing still uses a separate `AlphaHitThreshold = 0.01f`.

That is technical debt.
Rendering and picking should ultimately follow the same cutout contract.

### Vegetation picking only samples the base texture

The current picking/debug probe path checks the base tree/plant texture, not the optional overlay texture.
That is acceptable for the current canopy contract, but it is still an incomplete ownership boundary.

### Visible vegetation sync is still rebuild-oriented

`WorldRender3D.SyncTileSpriteBillboards(...)` reconstructs visible vegetation by scanning visible chunk snapshot tiles.
`VegetationInstanceRenderer.SyncVisibleInstances(...)` then regroups those instances into `MultiMesh` batches.

This works, but it still has avoidable allocation/perf debt on large visible areas.

### `VegetationInstanceRenderer` still has allocation-heavy LINQ in the hot path

The current batch regrouping uses `GroupBy(...).ToDictionary(...).ToList()`.
If you are working on performance in dense forests, this is one of the first places to inspect.

### Texture image caching is unbounded

`VegetationInstanceRenderer` keeps a static `Texture2D -> Image` cache for picking/probe helpers.
It currently has no eviction or lifecycle management.

### Old composite tree helpers still exist

`WorldSpriteVisuals.TryTreeWithPlantOverlay(...)` and `PixelArtFactory.GetTreeWithOverlay(...)` still exist for legacy/diagnostic use.
They are not the active 3D vegetation path.

Do not use them for new 3D tree rendering work.

### `PixelArtFactory.GetPlantOverlay(...)` is serving multiple consumers

It is currently used by:

- 3D vegetation billboards
- UI/resource icons
- some diagnostics

That shared use is convenient, but it means 3D cutout policy and UI/icon policy are still somewhat coupled.

## Smoke-Test / Debug Seams

Some debug methods exist mostly to support client smoke tests.
Examples:

- tree billboard texture probes
- tree billboard size probes
- tree billboard transparency/overlay pass probes
- generic billboard screen probes
- semantic hover target probes
- hover billboard/ring visibility probes
- resource billboard screen probes

Do not casually remove these seams unless you update the tests that depend on them.

When changing rendering behavior, check `ClientSmokeTests.cs` for expectations before deleting or renaming debug hooks.

## Practical Guidance For Future Agents

### Good changes in this folder usually look like this

- add a focused helper instead of growing `WorldRender3D`
- keep simulation logic in GameLogic
- keep render caches as presentation-only data
- use existing terrain smoothing/transition helpers instead of ad hoc neighbor logic
- preserve live-map queries where the runtime truth matters
- update `RENDERING.md` when contracts or ownership change

### Things to avoid

- putting gameplay logic into render caches or render-only classes
- adding renderer-only tree/terrain heuristics instead of fixing shared resolution logic
- baking canopy/tree detail back into the main 3D tree silhouette texture
- adding feature-specific highlight state directly into `VegetationInstanceRenderer`
- tinting shared actor/item billboard materials for hover emphasis
- reintroducing dead legacy billboard paths into `WorldRender3D`
- removing smoke-test debug hooks without replacing the coverage

### If you are debugging a visual bug, start here

- terrain blend bug around trees:
  inspect display-ground resolution, transition inputs, and live-vs-snapshot tile reads
- tree overlap artifact:
  inspect cutout texture generation, alpha contract, and overlay pass behavior
- wrong hovered tile on billboards:
  inspect picking order, hit-mask rules, and base-vs-overlay picking semantics
- large-map slowdown:
  inspect chunk build queueing, slice cache reuse, vegetation regrouping, and visible scan churn

## Short Version

If you only remember a few things, remember these:

- `WorldRender3D` orchestrates; helpers should own detailed behavior.
- Terrain is custom chunk meshing with shared texture arrays, not a Godot tilemap.
- Actors/items and vegetation are separate presentation systems for good reasons.
- Hover emphasis is separate again: semantic targets use `WorldHoverHighlightRenderer3D`, empty tiles use the generic overlay plate.
- Trees are base cutout billboards plus optional canopy/detail overlays, not composite textures in the active 3D path.
- Render caches are not gameplay truth.
- Keep this document updated when rendering ownership or contracts change.
