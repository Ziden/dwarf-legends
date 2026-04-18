# Vegetation Rendering Debt Cleanup Plan

## Summary

The current vegetation path is functional and materially better than the old highlight/composite approach, but it still carries architectural debt in four places:

- the render alpha contract and the picking alpha contract do not match
- the active 3D tree path still shares API surface with the old composite-tree model
- visible vegetation sync is still allocation-heavy and rebuild-oriented
- texture image caching and sprite generation are still too coupled across 3D and UI/2D consumers

This cleanup plan focuses on root-cause ownership and contracts first, then on hot-path performance. It does not change save data, GameLogic contracts, or the broad rendering architecture.

## Goals

- Make vegetation rendering, picking, and authored sprite alpha follow one explicit contract.
- Remove ambiguous old APIs so the 3D path has one canonical tree/plant presentation model.
- Reduce avoidable allocations and repeated rebuild work in visible vegetation sync.
- Decouple 3D billboard textures from UI/icon needs where those visual rules differ.
- Preserve the current batching direction: shared `MultiMesh` billboards for the main vegetation pass.

## Non-Goals

- No return to per-node tree billboards.
- No conversion of trees to 3D meshes in this pass.
- No new highlight system in this pass.
- No GameLogic-to-Godot coupling.

## Phase 1: Make The Vegetation Alpha Contract Explicit

### Problem

`VegetationInstanceRenderer` renders vegetation with `AlphaScissorThreshold = 0.5f`, but hover/probe picking still uses `AlphaHitThreshold = 0.01f`. That leaves rendering and picking with different visibility semantics.

### Changes

- Introduce one shared vegetation cutout threshold/contract used by:
  - base vegetation material creation
  - overlay material creation
  - hover picking
  - debug probe generation
- Replace the current ad hoc `0.01f` hit-testing threshold with the same cutout rule used by rendering.
- Decide and document overlay interaction policy:
  - default: picking should consider the union of base and overlay cutout masks
  - if overlay art is guaranteed to stay inside the base silhouette, still encode that as an explicit invariant in `RENDERING.md`
- Replace the misleading generic assumption around `OutlineOpaqueSilhouette` with a vegetation-specific flow that clearly states whether the silhouette is based on quantized alpha or raw source alpha.

### Target Files

- `src/DwarfFortress.GodotClient/Scripts/Rendering/VegetationInstanceRenderer.cs`
- `src/DwarfFortress.GodotClient/Scripts/Rendering/Visuals/PixelArtFactory.cs`
- `src/DwarfFortress.GodotClient/Scripts/Rendering/RENDERING.md`

## Phase 2: Remove Ambiguous Old Tree Composite APIs

### Problem

The active 3D renderer no longer wants composite tree textures, but the old API surface still exists through `WorldSpriteVisuals.TryTreeWithPlantOverlay(...)` and `PixelArtFactory.GetTreeWithOverlay(...)`. That keeps the old ownership model alive and makes future regressions more likely.

### Changes

- Remove 3D renderer dependencies on any composite tree helpers.
- Either delete the composite tree API entirely or move it behind an explicitly 2D/icon-only surface if diagnostics still need it.
- Split sprite factories by consumer intent:
  - 3D vegetation billboard textures
  - UI/resource icon textures
  - generic world sprite textures where shared rules still make sense
- Rename helpers so the ownership is obvious. For example:
  - `TryTreeCanopyOverlay(...)` remains 3D-specific
  - any retained composite tree helper should be named as a UI/icon helper, not a general tree visual helper

### Target Files

- `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldSpriteVisuals.cs`
- `src/DwarfFortress.GodotClient/Scripts/Rendering/Visuals/PixelArtFactory.cs`
- `src/DwarfFortress.GodotClient/Scripts/UI/SelectionResourceViewBuilder.cs`
- `src/DwarfFortress.GodotClient/Scripts/Diagnostics/ClientSmokeTests.cs`

## Phase 3: Reduce Vegetation Sync Churn

### Problem

`VegetationInstanceRenderer.SyncVisibleInstances(...)` rebuilds state with `GroupBy(...).ToDictionary(...).ToList()` every sync. `WorldRender3D.SyncTileSpriteBillboards(...)` also reconstructs visible vegetation by scanning chunk snapshot tiles.

### Changes

- Replace LINQ-based regrouping with reusable dictionaries/lists keyed by `VegetationBatchKey`.
- Reuse per-batch buffers instead of allocating new lists every sync.
- Keep batch ownership in `VegetationInstanceRenderer`, but move toward a clearer two-step flow:
  - `WorldRender3D` produces visible vegetation instances
  - `VegetationInstanceRenderer` incrementally syncs reusable batch buffers
- Evaluate whether the visible vegetation source should remain a full visible-chunk scan or move to a tracked visible vegetation index keyed by chunk/tile visibility.
- If index work is too broad for the first pass, still remove avoidable allocation churn in the existing scan path.

### Target Files

- `src/DwarfFortress.GodotClient/Scripts/Rendering/VegetationInstanceRenderer.cs`
- `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldRender3D.cs`

## Phase 4: Fix Cache Lifetime And Consumer Boundaries

### Problem

`TextureImageCache` in `VegetationInstanceRenderer` is static and unbounded. `PixelArtFactory.GetPlantOverlay(...)` now serves both 3D vegetation and UI/resource icon use cases, which risks coupling the 3D cutout policy into 2D/UI presentation.

### Changes

- Replace the unbounded static texture-image cache with lifecycle-managed caching:
  - clearable cache owned by the renderer, or
  - weak/lifecycle-aware cache keyed by texture RID
- Add explicit cache invalidation points on renderer reset/disposal.
- Split 3D vegetation billboard texture creation from UI/resource icon texture creation when their visual requirements differ.
- Keep shared lower-level palette/art generation helpers where that reuse is real, but do not force 3D cutout semantics onto UI assets by accident.

### Target Files

- `src/DwarfFortress.GodotClient/Scripts/Rendering/VegetationInstanceRenderer.cs`
- `src/DwarfFortress.GodotClient/Scripts/Rendering/Visuals/PixelArtFactory.cs`
- `src/DwarfFortress.GodotClient/Scripts/UI/SelectionResourceViewBuilder.cs`

## Validation Plan

- Add focused renderer tests proving vegetation picking uses the same cutout semantics as rendering.
- Add tests covering overlay interaction policy:
  - if overlay participates in picking, assert union behavior
  - if overlay is intentionally non-pickable, assert it stays inside the base silhouette
- Add regression tests proving the 3D tree path never uses the old composite tree texture helper.
- Add focused tests for vegetation batch sync to assert stable batch counts without LINQ-driven churn assumptions.
- Build:
  - `dotnet build .\\src\\DwarfFortress.GodotClient\\DwarfFortress.GodotClient.csproj`
- Run focused smoke:
  - `tree-species-billboard`
  - `resource-billboard-hover`
  - `resource-billboard-designation`
  - any tree overlap/picking smoke added in this pass

## Recommended Execution Order

1. Phase 1: unify render/picking alpha semantics and document the contract
2. Phase 2: remove or isolate the composite-tree API
3. Phase 3: reduce batch sync allocation churn
4. Phase 4: fix cache lifetime and 2D/3D consumer separation

## Success Criteria

- Vegetation picking and rendering agree on what counts as visible/clickable vegetation.
- The active 3D tree path has one canonical asset/presentation model.
- Vegetation sync stops allocating heavily on every refresh.
- Cache ownership is explicit and bounded.
- `RENDERING.md` clearly explains the vegetation pipeline and its contracts.
