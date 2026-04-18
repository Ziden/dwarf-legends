# 3D Tilemap Performance Implementation Log

## Goal

Implement the approved 3D tilemap performance plan without changing GameLogic save/runtime contracts, while preserving current terrain visuals and sprite-based actor presentation.

## Crash Recovery Notes

- This file is updated at each major milestone so work can be recovered if the IDE crashes.
- The repository already had unrelated and in-progress changes when this work started. Those changes must be preserved.

## Worktree Snapshot At Start

- Existing modified files were present in both `DwarfFortress.GameLogic` and `DwarfFortress.GodotClient`.
- Relevant renderer files already modified before this task:
  - `src/DwarfFortress.GodotClient/Scripts/App/GameRoot.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Diagnostics/ClientSmokeTests.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Diagnostics/DebugProfilerMonitors.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldCamera3DController.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldChunkRenderSnapshot.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldChunkSliceMesher.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldRender3D.cs`
  - `src/DwarfFortress.GodotClient/Scripts/Rendering/WorldSliceHoverResolver3D.cs`

## Milestones

1. Terrain surface pipeline split:
   - `TerrainSurfaceRecipeBuilder`
   - `TerrainSurfaceCanvasCache`
   - `TerrainSurfaceArrayLibrary`
   - stats wiring
2. Chunk slice build optimization:
   - slice-local cached render inputs
   - direct packed-array mesh construction
   - chunk queue prioritization and stats
3. Vegetation batching:
   - replace tree/plant node billboards with `MultiMeshInstance3D`
   - preserve hover and designation behavior
4. Instrumentation and tests:
   - debug monitor extensions
   - focused terrain cache tests
   - chunk mesh regression test
   - smoke coverage updates
5. Validation:
   - targeted builds/tests
   - results recorded here

## Progress

### 2026-04-14 Initial checkpoint

- Created persisted implementation log.
- Confirmed terrain already uses a shared `Texture2DArray`; the main duplicate allocation issue is the combined 2D/3D terrain cache path.
- Confirmed chunk terrain is rebuilt tile-by-tile into new `ArrayMesh` surfaces and vegetation still uses node-per-instance billboards.
- Next step: inspect current uncommitted renderer files and refactor terrain surface caching first.

### 2026-04-14 Terrain cache split checkpoint

- Added `TerrainSurfaceRecipeBuilder` to own terrain surface recipe construction and image composition.
- Split terrain caching into:
  - `TerrainSurfaceCanvasCache` for 2D `Texture2D` consumers
  - `TerrainSurfaceArrayLibrary` for 3D array layers without standalone per-recipe textures
- Replaced `TileSurfaceLibrary` with a small facade over the new 2D/3D caches.
- Increased 3D terrain array initial capacity to `256`.
- Increased terrain detail array initial capacity to `64`.
- Added `TerrainRenderStats` and detail-array stat accessors as the shared telemetry seam for the next milestones.
- Next step: refactor chunk slice build inputs and direct mesh array generation.

### 2026-04-14 Chunk mesher checkpoint

- Added `WorldChunkSliceRenderCache` to compute per-slice tile render inputs once and reuse them during terrain meshing.
- Replaced `SurfaceTool` terrain building in `WorldChunkSliceMesher` with direct mesh-array builders for:
  - textured top surfaces
  - colored side surfaces
  - textured detail surfaces
- Extended chunk mesh build output with top/side/detail vertex counts.
- Updated `WorldRender3D` to:
  - prioritize pending chunk builds by camera focus
  - feed the new slice cache into the mesher
  - record chunk build counters into `TerrainRenderStats`
- Verified the Godot client project builds after the terrain/cache and chunk mesher refactors.
- Next step: replace tree and plant resource billboards with batched vegetation instances while preserving hover and designation behavior.

### 2026-04-14 Vegetation and smoke coverage checkpoint

- Batched tree and plant rendering through `VegetationInstanceRenderer` using `MultiMeshInstance3D` groups keyed by shared texture/material.
- Preserved CPU-side tile lookup for hover/debug access and routed resource billboard highlight state through the batched vegetation path.
- Added a hover-state cleanup seam so actor billboard hover clears vegetation hover highlights instead of leaving stale resource outlines active.
- Extended `ClientSmokeTests` with:
  - terrain-array reuse/no-standalone-texture coverage
  - terrain array growth stability coverage
  - chunk slice mesher regression coverage
  - large-view render residency metric capture for `render3d_sync_slice`, `tile_sprites`, `billboards`, visible chunk count, and terrain layer count
- Next step: run targeted build/tests and record validation results.

### 2026-04-14 Live vegetation sync fix

- Root-caused a designation regression in the batched vegetation path:
  - live tile changes were not rebuilding vegetation batches while chunk preview streaming was active
  - forcing full dirty snapshot recapture fixed designation state but starved chunk residency because it kept refeeding the bounded chunk build queue
- Final fix:
  - vegetation billboards now resolve live tile state from `WorldMap` for visible non-preview chunks
  - preview chunks still use streamed chunk snapshots
  - when the streaming path clears dirty live chunks, it now marks tile-sprite billboards dirty so the vegetation batch is rebuilt on the next dynamic render sync
  - this keeps designation/tree-state visuals live without re-invalidating terrain chunk meshes

### 2026-04-14 Validation

- `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
  - passed
- Focused Godot headless smoke:
  - command: `DF_SMOKE_FILTER=chunk-slice-mesher,render-mode-residency,billboard-hover,resource-billboard-hover,resource-billboard-area-selection,resource-billboard-designation,tree-species-billboard`
  - command body: `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
  - passed
- Focused smoke metrics from the passing run:
  - large-view: `render3d_sync_slice=0.0050s`, `tile_sprites=0.0103s`, `billboards=0.0021s`, `visible_chunks=100`, `terrain_layers=1998`
  - preview-jump: `render3d_sync_slice=0.0049s`, `tile_sprites=0.0020s`, `billboards=0.0005s`, `visible_chunks=25`, `terrain_layers=2000`
  - return-local: `render3d_sync_slice=0.0085s`, `tile_sprites=0.0025s`, `billboards=0.0006s`, `visible_chunks=25`, `terrain_layers=2000`
- Additional targeted smoke:
  - `DF_SMOKE_FILTER=resource-billboard-designation`
  - passed after the live vegetation dirty-path fix
- Known unrelated baseline issue observed during validation:
  - `DF_SMOKE_FILTER=pixel-art,...` fails in `RunPixelArtFactoryTest` with `Expected creature 'cat' to have a distinct walking silhouette from its idle pose.`
  - this failure predates the renderer changes validated above and was not modified in this work.

### 2026-04-14 Tree display-ground regression checkpoint

- Added shared GameLogic terrain-clearing resolution in `TerrainClearedGroundResolver` so renderer and gameplay can use the same cleared-ground rule.
- Refactored `TerrainClearanceHelper` into a thin adapter over the shared resolver.
- Added slice-local display-ground caching in `WorldChunkSliceRenderCache` keyed by world `(x, y, z)` alongside cached `TileRenderData`.
- Replaced tree underlay fallback behavior with canonical display-ground resolution in `TerrainGroundResolver`.
- Switched terrain transition resolution to operate on resolved display ground for neighbors, so tree tiles now participate in blending as their cleared ground instead of disappearing from smoothing.
- Kept liquid ground fallback behavior intact while routing liquid transitions through the same display-ground neighbor accessor.
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
  - passed
- Next step: replace the scaled duplicate hover quads with a shared thin alpha-outline material path across vegetation, resource, and actor/item billboards.

### 2026-04-14 Billboard outline checkpoint

- Added shared outline material caching in `BillboardOutlineMaterialLibrary`.
- Added `Graphics/Shaders/BillboardOutline.gdshader`:
  - billboard-aware spatial shader
  - outline-only alpha rendering from 8-neighbor texture samples
  - no filled duplicate sprite body
- Switched the following paths from scaled tinted duplicate textures to the shared outline shader:
  - `VegetationInstanceRenderer`
  - `WorldActorPresentation3D`
  - `WorldRender3D` resource billboards
- Standardized hover/designation overlay pad to `1.03f` so the outline stays tight instead of producing a visibly larger duplicate.
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
  - passed
- Next step: add focused parity/regression coverage for tree display-ground resolution, tree-adjacent blends, and the new hover outline path, then run targeted smoke.

### 2026-04-14 Regression coverage and validation checkpoint

- Added focused GameLogic tests in `TerrainClearedGroundResolverTests` for:
  - dirt-backed cleared terrain resolution
  - stone-backed fallback resolution
- Added focused client smoke coverage in `ClientSmokeTests` for:
  - tree display-ground resolution
  - tree-adjacent blend regression
  - actor/item hover outlines using the shared shader outline path
  - resource hover outlines using the shared shader outline path
- Added debug accessors needed to verify:
  - outline material type is `ShaderMaterial`
  - hover outline scale stays at `1.03f`
- Validation run results:
  - `dotnet test .\src\DwarfFortress.Tests\DwarfFortress.Tests.csproj --filter TerrainClearedGroundResolver`
    - passed (`2/2`)
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
    - passed
  - Focused Godot smoke:
    - `DF_SMOKE_FILTER=tree-display-ground,tree-blend-regression,chunk-slice-mesher,billboard-hover,resource-billboard-hover,resource-billboard-area-selection,resource-billboard-designation`
    - command: `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
    - passed
- Notes:
  - `dotnet test` emitted existing xUnit analyzer warnings in unrelated files, but the targeted test run passed.

### 2026-04-14 Tree hover/tile alignment checkpoint

- Investigated the reported tree hover offset and smaller-looking tree sprites after the vegetation batching change.
- Root cause identified in the batched vegetation renderer:
  - tree and plant batches were sharing a unit quad mesh
  - per-instance size was being encoded through the instance transform scale
  - the old node-per-billboard path sized the mesh itself (`QuadMesh.Size = visual.WorldSize`) and only used transform translation for anchoring
- Updated `VegetationInstanceRenderer` so vegetation batches are grouped by `(kind, texture, size)` and each batch owns a correctly sized `QuadMesh`.
- Updated vegetation instance transforms to use mesh size for the billboard dimensions and only use transform scale for the small outline pad multiplier.
- Added debug accessors in `WorldRender3D`/`VegetationInstanceRenderer` to expose:
  - rendered tree billboard size
  - a stable screen probe for a tree billboard
- Extended `ClientSmokeTests.RunTreeSpeciesBillboardRenderTest()` to verify:
  - injected tree batches still render at `1x2`
  - hover from the projected tree billboard resolves the exact injected tile through the vegetation hover path
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
  - passed
- Next step: run focused headless Godot smoke for `tree-species-billboard`, `billboard-hover`, and `resource-billboard-hover` to confirm the tree hover regression is closed end-to-end.

### 2026-04-14 Vegetation hover hit-test checkpoint

- Follow-up validation exposed a remaining root issue in vegetation hover resolution:
  - mesh sizing was fixed, but vegetation picking still accepted any point inside the projected billboard rectangle
  - transparent canopy space and overlapping billboards could still resolve the wrong vegetation tile
- Updated `VegetationInstanceRenderer` to use cached source `Image` data per billboard texture and reject hover hits on transparent pixels.
- Added stable opaque probe search for vegetation smoke/debug helpers so tree/resource probe points come from opaque sprite pixels instead of the billboard center.
- Tightened `WorldRender3D` vegetation debug probes so they only return a screen point if the full hover pipeline resolves:
  - the same tile
  - `HoverSelectionMode.RawTile`
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
    - passed
  - Focused direct Godot smoke:
    - `DF_SMOKE_FILTER=tree-species-billboard,billboard-hover,resource-billboard-hover,resource-billboard-area-selection,resource-billboard-designation`
    - command: `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
    - passed (`EXIT_CODE=0`)

### 2026-04-14 Vegetation emphasis architecture investigation

- Investigated the new report that hovering one tree appears to affect all trees of the same visual type.
- Findings:
  - the main vegetation visuals are correctly shared through `VegetationInstanceRenderer` batches keyed by `(kind, texture, size)`
  - vegetation hover, designation, area selection, picking, and debug probe logic are also living inside `VegetationInstanceRenderer`
  - `WorldRender3D` still retains the pre-batching tree/plant billboard state and helpers (`_treeBillboards`, `_plantBillboards`, `_hoveredResourceBillboard`, legacy `BillboardState` helpers), even though the active vegetation path now uses `VegetationInstanceRenderer`
  - this leaves vegetation rendering split across:
    - `WorldRender3D`
    - `VegetationInstanceRenderer`
    - `InputController`
    - leftover dead resource-billboard code from the pre-batched path
- Architectural conclusion:
  - the main shared vegetation batch should stay shared
  - hover/selection/designation emphasis should stop piggybacking on the shared vegetation batch renderer
  - the maintainable fix is to split vegetation into:
    - a pure shared main-billboard batch renderer
    - a dedicated vegetation emphasis renderer keyed by tile, responsible only for hover/selection/designation overlays
    - a dedicated vegetation picker / visible-instance index used by both
- Recommended emphasis strategy:
  - keep all main vegetation billboards in shared `MultiMesh` batches
  - render hover/selection/designation with separate billboard overlay nodes or a tiny overlay cache keyed by tile
  - overlays should be allocated only for emphasized visible tiles, not for every visible tree/plant
  - this preserves batching on the hot path while giving emphasis a stable per-tile identity
- Cleanup required in the implementation pass:
  - remove the dead pre-batching tree/plant billboard state and helpers from `WorldRender3D`
  - move vegetation-specific hover/selection state ownership out of the main shared batch renderer
  - keep picking/probe code read-only and separate from emphasis rendering state

### 2026-04-14 Rendering cleanup refactor plan

- Agreed direction for the next pass:
  - remove current entity-highlight / billboard-emphasis code first
  - organize the rendering folder and write a `RENDERING.md` guide before rebuilding emphasis
- Planned default scope for "entity highlight code":
  - vegetation hover/designation/selection emphasis
  - actor/item billboard hover emphasis
  - shared billboard outline shader/material plumbing used only for those entity/billboard overlays
  - not the terrain/tile selection plates or other world overlay meshes
- Planned sequence:
  1. Add `Scripts/Rendering/RENDERING.md` documenting the current rendering architecture and the target ownership boundaries after cleanup.
  2. Remove entity highlight rendering from `VegetationInstanceRenderer` and `WorldActorPresentation3D`, leaving picking and main visual rendering intact.
  3. Delete the dead legacy tree/plant billboard highlight path from `WorldRender3D`.
  4. Simplify the public debug/validation surface so there is no highlight-specific API left except what is still needed for non-highlight rendering tests.
  5. Re-run focused smoke/build validation with no entity highlight feature active.
  6. In a later pass, rebuild emphasis on clean seams with a dedicated emphasis renderer instead of mixing it into shared batches.

### 2026-04-14 Entity highlight removal checkpoint

- Saved the standalone cleanup plan in `artifacts/RENDERING_CLEANUP_PLAN.md`.
- Added `Scripts/Rendering/RENDERING.md` documenting:
  - renderer ownership
  - the current world render flow
  - terrain vs actor vs vegetation vs overlay responsibilities
  - the cleanup target that separates main rendering from future emphasis rendering
- Removed vegetation entity emphasis from `VegetationInstanceRenderer`:
  - removed hover/designation/area-selection overlay batches
  - removed vegetation-owned hover/emphasis state
  - kept shared vegetation `MultiMesh` rendering
  - kept alpha-aware vegetation picking and debug probe support
- Removed actor/item hover emphasis from `WorldActorPresentation3D`:
  - removed outline mesh/material state from billboard states
  - kept billboard rendering
  - kept actor/item billboard picking
- Removed dead legacy tree/plant billboard highlight plumbing from `WorldRender3D`:
  - removed legacy resource billboard state and helper methods
  - removed old debug emphasis APIs
  - kept billboard selection routing and vegetation debug probe routing
- Deleted unused shared outline assets:
  - `Scripts/Rendering/BillboardOutlineMaterialLibrary.cs`
  - `Graphics/Shaders/BillboardOutline.gdshader`
- Updated focused smoke expectations in `ClientSmokeTests` so they validate picking/selection behavior without depending on entity outline visuals.
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
    - passed
- Next step:
  - run focused Godot smoke for actor/item billboard selection, tree/resource billboard selection, area selection, and tree designation on the no-highlight baseline

### 2026-04-14 No-highlight baseline validation

- Focused direct Godot smoke passed on the cleaned baseline with entity highlight rendering removed.
- Validation run:
  - `DF_SMOKE_FILTER=tree-species-billboard,billboard-hover,resource-billboard-hover,resource-billboard-area-selection,resource-billboard-designation`
  - command: `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
  - result: passed (`EXIT_CODE=0`)
- Focused smoke expectations were updated to validate:
  - actor/item billboard picking and selection
  - vegetation billboard picking and raw-tile selection
  - area selection persistence without entity emphasis visuals
  - tree designation state and post-designation pickability without entity emphasis visuals
- The codebase now has a documented no-highlight rendering baseline suitable for rebuilding entity emphasis on cleaner seams in a later pass.

### 2026-04-14 Vegetation z-fighting investigation

- Investigated the remaining tree "z-fighting" report in the current batched vegetation renderer.
- Root cause identified in the vegetation material path:
  - trees and plants are rendered through shared `MultiMesh` batches in `VegetationInstanceRenderer`
  - those batches were still using plain `TransparencyEnum.Alpha`
  - overlapping alpha-blended billboards inside one shared `MultiMesh` are not depth-stable, which reads like z-fighting in dense trees
- Applied a focused renderer fix:
  - switched shared vegetation billboard materials from plain alpha blending to `TransparencyEnum.AlphaDepthPrePass`
  - kept the batching architecture and alpha-aware picking unchanged
  - added a small debug seam so smoke coverage can assert the intended vegetation transparency mode
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
    - passed
  - `DF_SMOKE_FILTER=tree-species-billboard`
  - command: `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
    - passed
- Current conclusion:
  - the remaining tree artifact was most likely transparency sorting instability in the shared vegetation `MultiMesh`, not legacy highlight code or terrain blend logic
  - if artifacts remain after this change, the next suspect is duplicate vegetation emission for the same tile-kind, not the material path

### 2026-04-15 Vegetation cutout pipeline checkpoint

- Follow-up investigation on overlapping trees found a second vegetation rendering debt:
  - tree and plant billboard textures were authored through generic outlined texture helpers that leave near-opaque alpha values such as `0.95`
  - tree canopy details were still baked into composite billboard textures via `GetTreeWithOverlay(...)`
  - in 3D that mixes silhouette/depth semantics with decorative overlay art and causes front-tree outline pixels to read as partially transparent
- Implementation direction for the fix:
  - switch vegetation sprite generation to a dedicated cutout texture helper that quantizes vegetation alpha to binary before rendering
  - switch shared vegetation billboards from depth-prepass alpha blending to alpha-scissor cutout materials
  - keep tree canopy/fruit art separate from the base tree texture by routing it as an optional second pass on the same shared vegetation material instead of baking it into a composed billboard texture
  - extend focused smoke coverage so canopy trees assert both the base species texture and the presence of a separate overlay pass
- Implemented for this checkpoint:
  - `PixelArtFactory` now routes tree and plant overlay textures through a vegetation-specific cutout texture helper instead of the generic semi-transparent outlined texture path
  - `VegetationInstanceRenderer` now batches vegetation as alpha-scissor cutout billboards and can attach a second-pass canopy overlay material on the same shared billboard geometry
  - `WorldRender3D` now stops baking canopy overlays into composite tree textures for the 3D vegetation path and instead passes the canopy texture separately into the vegetation batch instance
  - `RENDERING.md` now documents the vegetation base/overlay ownership contract
  - focused smoke now asserts:
    - tree billboards use cutout rendering
    - canopy trees keep the base species texture
    - canopy trees expose a separate overlay pass
- Validation for this checkpoint:
  - `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
    - passed
  - focused smoke:
    - `DF_SMOKE_FILTER=tree-species-billboard,resource-billboard-designation`
    - command: `Godot_v4.6.1-stable_mono_win64_console.exe --headless --path src/DwarfFortress.GodotClient --scene res://Tests/ClientSmokeTests.tscn`
    - passed
  - note:
    - a broader smoke including `pixel-art` still hits the unrelated pre-existing baseline failure `Expected creature 'cat' to have a distinct walking silhouette from its idle pose.`

### 2026-04-15 Vegetation area debt audit

- Investigated the remaining vegetation renderer and sprite-pipeline seams after the cutout conversion.
- No obvious ad hoc z-bias hacks or one-off tree overlap workarounds were found in the current path.
- Confirmed remaining technical debt in the area:
  - `VegetationInstanceRenderer` still uses a different alpha threshold for picking (`0.01f`) than for rendering (`0.5f` alpha scissor), so hit testing and visible cutout semantics can drift again if future vegetation art reintroduces semi-alpha pixels.
  - vegetation picking and debug probe logic still sample only the base tree texture and ignore the optional overlay texture, which is fine for the current canopy contract but is still an incomplete interaction contract.
  - vegetation batch sync still rebuilds visible states with `GroupBy(...).ToDictionary(...).ToList()` on every refresh, which is allocation-heavy in a hot render path.
  - `TextureImageCache` in `VegetationInstanceRenderer` is unbounded and has no eviction or lifecycle management for generated texture images.
  - `WorldRender3D.SyncTileSpriteBillboards(...)` still reconstructs visible vegetation by scanning all visible chunk snapshot tiles instead of syncing from a tracked visible vegetation index.
  - the old composite tree API still exists in `WorldSpriteVisuals.TryTreeWithPlantOverlay(...)` and `PixelArtFactory.GetTreeWithOverlay(...)`; it is no longer the active 3D path but it keeps the old ownership model alive in the API surface and tests.
  - `PixelArtFactory.GetPlantOverlay(...)` now serves both 3D vegetation billboards and 2D/UI icon consumers, which couples the 3D cutout alpha policy to UI/resource icon presentation.
- Recommended follow-up cleanup order:
  - unify vegetation picking with the same cutout alpha contract used by rendering
  - remove or isolate the old composite tree API from the active 3D renderer surface
  - replace allocation-heavy vegetation regrouping with reusable batch buckets
  - introduce bounded or lifecycle-managed texture-image caching
  - split 3D vegetation billboard textures from 2D/UI presentation textures if the visual requirements diverge further

### 2026-04-15 Vegetation debt cleanup planning

- Wrote a concrete follow-up cleanup plan in `artifacts/VEGETATION_TECH_DEBT_CLEANUP_PLAN.md`.
- The plan is intentionally ordered around root-cause contracts first, then hot-path performance:
  - Phase 1: unify vegetation render and picking alpha semantics
  - Phase 2: remove or isolate the old composite-tree API
  - Phase 3: reduce allocation churn in vegetation sync
  - Phase 4: bound cache lifetime and separate 3D billboard textures from UI/icon consumers where needed
- No renderer behavior changed in this planning step.

### 2026-04-15 Rendering guide refresh

- Rewrote `src/DwarfFortress.GodotClient/Scripts/Rendering/RENDERING.md` to describe the current rendering architecture instead of only the high-level ownership summary.
- Documented:
  - the active `SyncSlice(...)` vs `SyncDynamicState(...)` frame flow
  - current ownership boundaries for terrain, actor/item billboards, and vegetation billboards
  - the folder/file map for the rendering area
  - important runtime/render-cache/source-of-truth contracts
  - current render priority and vegetation cutout behavior
  - active quirks and technical debt that future agents should not accidentally extend
  - smoke-test/debug seams that should be preserved intentionally
- No runtime rendering behavior changed in this documentation step.
