# 3D Migration Plan

## Goal

Migrate the Godot client from a 2D sprite-driven renderer to a 3D presentation layer while preserving the existing simulation boundary in `DwarfFortress.GameLogic`.

This plan assumes:

- `DwarfFortress.GameLogic` remains engine-agnostic and continues to own simulation state.
- `DwarfFortress.GodotClient` is the primary migration surface.
- The current sprite workflow is removed rather than maintained in parallel long term.

## Current State Summary

The repository is already structurally favorable for a 3D migration:

- Simulation and rendering are separated.
- The Godot client currently renders through `Node2D`, `Camera2D`, and manual `DrawTextureRect` calls.
- Visual content is still keyed off sprite lookups and generated 2D fallback textures.
- The content editor includes a full sprite-mapping workflow that will become obsolete.

Observed repo scope at the time of planning:

- Roughly 71 content IDs across the core config bundles scanned for migration sizing.
- 42 explicit sprite-map entries under the current `sprites/` JSON files.
- Sprite-specific editor/runtime pieces exist in the content editor, validator, app settings, and tests.

## What Should Change

### Keep

- Data-driven simulation definitions in `data/ConfigBundle/`
- `DwarfFortress.GameLogic` as the source of truth for world state
- Snapshot-based client rendering
- Existing gameplay systems unless a 3D presentation requirement forces a contract change

### Replace

- 2D tile drawing with 3D terrain, props, and actors
- Sprite-sheet mappings with 3D asset references, procedural mesh generation, or archetype-based model lookup
- Sprite preview/editor tooling with either:
  - no visual editor at all, if asset references are simple enough to edit in JSON, or
  - a new 3D asset catalog/preview workflow

### Remove

- Sprite mapper UI
- Sprite services and sheet path resolution
- Sprite-specific validation rules
- Sprite-related configuration keys and static file hosting
- Sprite-related tests
- Sprite JSON manifests once 3D asset references fully replace them

## Migration Strategy

Do not treat this as "replace every sprite with a unique 3D model" from day one. That is the slowest and riskiest path.

Instead, split the work into two tracks that move in parallel:

1. Engine/client migration
2. Asset migration

The client should become capable of rendering 3D long before every asset is final.

## Target Visual Architecture

## 1. Rendering Model

Move the Godot client to a 3D scene built around:

- `Node3D` root instead of `Node2D`
- `Camera3D` with either:
  - isometric orthographic projection, or
  - slightly pitched perspective projection
- `MeshInstance3D` and `MultiMeshInstance3D` for tiles and repeated props
- optional `GridMap` only if it matches performance and flexibility requirements; otherwise use custom chunk meshing

Recommended direction:

- Use `Camera3D` in orthographic mode for readability.
- Use chunked procedural mesh generation for terrain/walls/floors.
- Use instanced scenes for dwarves, creatures, items, and buildings.

This preserves the readability of a fortress sim while still being fully 3D.

## 2. World Representation

The simulation already uses 3D coordinates and z-levels. The visual layer should map each logical tile to a world-space cell:

- `X`, `Y`: horizontal placement
- `Z`: layer height in world units

Recommended rendering rules:

- Floors: thin top surfaces with optional trim
- Walls: full-height block meshes
- Ramps/stairs: dedicated modular meshes
- Fluids: animated surface meshes with level-based height offsets
- Hidden levels: clipped, faded, or cut away above the viewed z-level

## 3. Input and Camera

Current input assumes 2D screen-to-tile mapping. In 3D, replace this with:

- raycast-based tile picking
- camera orbit/pan/zoom controls
- z-level visibility controls rather than pure 2D layer switching
- optional cutaway/ghost rendering for levels above the active layer

## Asset Strategy

## 1. Do Not Make Every Asset Unique

Not every current sprite should become a bespoke model.

Use these buckets instead:

### Bucket A: Procedural or parametric assets

Use mesh generation, material swaps, or simple modular kits for:

- stone floors
- soil floors
- walls by material family
- water and magma surfaces
- simple stockpile markers
- designation overlays

These should not require individual authored models per definition.

### Bucket B: Archetype models with material variants

Use one mesh plus material or decal variants for:

- boulders
- ore chunks
- bars
- logs
- planks
- crates/barrels/buckets
- generic furniture families

This avoids creating a unique model for every item definition.

### Bucket C: Unique authored assets

Reserve custom models for high-value visuals:

- dwarves
- distinct creature families
- workshops
- major buildings
- hero props the player reads at a glance

### Bucket D: Effects and presentation-only assets

Create separate 3D assets for:

- mining dust
- smoke/steam
- fluid splashes
- selection markers
- job/designation indicators

## 2. Recommended Initial Asset Inventory

A sensible first playable 3D set is not 71 unique models. It is closer to:

- 8 to 12 terrain modules
- 10 to 20 item archetype meshes
- 3 to 5 dwarf and humanoid rigs or variants
- 5 to 10 creature archetypes
- 5 to 8 workshop/building meshes
- a small VFX pack for feedback

After that, expand by gameplay importance.

## 3. Asset Source Decision

Pick one of these deliberately before production starts:

### Option A: Low-poly custom 3D pack

Pros:

- Fastest route to a coherent 3D game
- Easy to optimize
- Easiest to expand gradually

Cons:

- Larger stylistic departure from classic DF visuals

### Option B: 2.5D billboards in a 3D world

Pros:

- Fastest migration
- Reuses some existing art logic
- Lower asset production burden

Cons:

- You are still partly in sprite land
- Conflicts with the stated goal of removing sprite tooling

### Option C: Fully modeled 3D assets for all major content

Pros:

- Strongest visual identity if executed well

Cons:

- Highest art cost
- Highest rigging and animation cost
- Slowest time to first playable 3D build

Recommended choice for this repo:

- Option A for environment, buildings, and props
- Minimal rigged character set for dwarves and core creatures
- No continued investment in sprite tooling

## Data and Runtime Changes

## 1. Replace Sprite Lookup Contracts

Current runtime visual lookup is centered around sprite registries and 2D texture factories.

Replace with a visual asset layer such as:

- `VisualDefinitionRegistry`
- `ModelRegistry`
- `MaterialVariantRegistry`
- `EntitySceneRegistry`

Each runtime-visible definition should resolve through one of these patterns:

- `model path`
- `scene path`
- `mesh archetype + material variant`
- `procedural generator key`

## 2. Introduce 3D Visual Metadata

Add visual metadata in one of two ways:

### Preferred

Add visual fields directly to content definitions where the ownership is obvious.

Examples:

- tiles: surface style, wall style, material family
- items: prop archetype
- creatures: actor scene or rig family
- buildings: scene or construction kit ID

### Alternative

Create a dedicated visual manifest directory such as `data/VisualBundle/`.

Use this only if you want to keep simulation JSON completely separate from presentation JSON.

## 3. Chunk Rendering

Add chunked 3D renderers in the Godot client:

- `TerrainChunkRenderer3D`
- `BuildingRenderer3D`
- `EntityRenderer3D`
- `ItemRenderer3D`
- `EffectRenderer3D`

These should consume snapshots, not simulation internals.

## Content Editor Impact

## 1. Remove Sprite Workflow

The current content editor contains sprite-specific functionality that should be retired as part of the migration.

That includes removing or replacing:

- sprite mapper page
- sprite nav section and home-page card
- sprite services
- sprite sheet static file hosting
- sprite validation rules
- sprite models used only by the editor
- sprite-focused tests
- sprite configuration keys in app settings

Concrete repo areas affected:

- `src/DwarfFortress.ContentEditor/Components/Pages/SpriteMapper.razor`
- `src/DwarfFortress.ContentEditor/Components/Layout/NavMenu.razor`
- `src/DwarfFortress.ContentEditor/Components/Pages/Home.razor`
- `src/DwarfFortress.ContentEditor/Services/SpriteService.cs`
- `src/DwarfFortress.ContentEditor/Services/SpriteSheetService.cs`
- `src/DwarfFortress.ContentEditor/Services/ValidationService.cs`
- `src/DwarfFortress.ContentEditor/Models/SpriteMapModel.cs`
- `src/DwarfFortress.ContentEditor/Program.cs`
- `src/DwarfFortress.ContentEditor/appsettings.json`
- sprite-related tests in `src/DwarfFortress.ContentEditor.Tests/`

## 2. Decide What Replaces It

Pick one of these paths:

### Path A: No replacement editor

Use JSON fields for model and scene references and keep editing text-first.

Best when:

- asset references are stable
- the team is comfortable editing JSON
- you want the smallest editor surface

### Path B: 3D asset catalog page

Replace the sprite mapper with a simpler tool that:

- lists available 3D assets
- previews scenes or glTF models
- assigns model IDs instead of sprite coordinates
- validates missing or broken asset references

Best when:

- non-programmers need to wire assets often
- the asset library will grow quickly

Recommended choice:

- remove the sprite mapper first
- only build a 3D asset editor if manual JSON editing becomes a real bottleneck

## 3. Validator Rewrite

Replace sprite validation with asset validation:

- referenced model exists
- referenced scene exists
- optional material variant exists
- optional animation set exists
- definitions do not point at deprecated sprite fields

## Phased Delivery Plan

## Phase 0: Preproduction and Decision Lock

Duration: 1 to 2 weeks

Deliverables:

- final camera/readability decision
- target art style chosen
- asset naming convention
- decision on where 3D visual metadata lives
- one written deprecation plan for sprite tooling

Exit criteria:

- no unresolved debate about orthographic vs perspective
- no unresolved debate about JSON visual schema

## Phase 1: 3D Technical Spike

Duration: 1 to 2 weeks

Build a thin prototype that renders:

- floor tiles
- walls
- one dwarf actor
- one building
- z-level visibility controls
- raycast tile selection

Do not wait for finished art. Use blockout meshes.

Exit criteria:

- fortress readability is acceptable
- camera and picking are reliable
- chunk performance is measured on representative map sizes

## Phase 2: Rendering Foundation

Duration: 2 to 4 weeks

Implement:

- `GameRoot` replacement for 3D scene graph
- terrain chunk meshing or instancing
- 3D camera controller
- selection/highlight system
- z-level cutaway visualization
- basic lighting and shadows

Exit criteria:

- world renders fully in 3D using placeholder assets
- current core gameplay loop remains playable

## Phase 3: Asset Contract Migration

Duration: 1 to 2 weeks

Implement:

- new visual metadata schema
- runtime registry for models/scenes/material variants
- migration script or manual conversion plan from sprite maps to 3D mappings
- backward compatibility layer only if needed temporarily

Exit criteria:

- runtime no longer depends on sprite coordinates for migrated content

## Phase 4: Content Editor Cleanup

Duration: 1 week

Implement:

- remove sprite mapper route and UI links
- remove sprite service registrations from DI
- remove `/sprite-art` static hosting
- remove sprite-specific app settings
- replace validator rules
- delete or replace sprite-focused tests

Exit criteria:

- content editor builds and tests without sprite infrastructure
- editor only validates active 3D-era data contracts

## Phase 5: First Playable 3D Content Pack

Duration: 3 to 6 weeks

Create enough assets for a coherent playable slice:

- terrain kit
- dwarf model and animation set
- essential creatures
- starter items and props
- starter workshops/buildings
- feedback VFX

Focus on the content needed for the first 30 minutes of play.

Exit criteria:

- new embark is fully playable in 3D for the core economy loop

## Phase 6: Full Asset Coverage

Duration: ongoing

Expand coverage based on gameplay importance:

- commonly seen items first
- commonly seen creatures second
- rare content later

Not all missing long-tail assets need to block the migration. Use fallback archetypes while coverage grows.

## Phase 7: Optimization and Polish

Duration: ongoing

Optimize:

- chunk rebuild frequency
- instancing strategy
- LODs where useful
- culling and hidden-layer rendering
- animation budget
- VFX budget

Polish:

- readability tuning
- UI overlays adapted for 3D space
- lighting balance
- material consistency

## Recommended Team Breakdown

### Engineering

- 3D client architecture
- render pipeline
- input/picking
- data contract migration
- content editor cleanup
- validator rewrite

### Technical Art

- modular terrain kit
- material library
- rigging conventions
- import/export pipeline
- naming conventions

### Art

- dwarf and creature models
- buildings/workshops
- prop library
- VFX assets

## Risks and Mitigations

## Risk: readability drops in 3D

Mitigation:

- use orthographic camera
- keep silhouettes exaggerated
- test cutaway views early
- prototype with real gameplay before committing to art production

## Risk: asset scope explodes

Mitigation:

- use archetypes and material variants
- keep long-tail content on generic fallback meshes
- do not require 1:1 bespoke replacement for every current sprite entry

## Risk: editor and runtime drift during transition

Mitigation:

- define the new visual schema once
- migrate validator immediately after schema change
- remove sprite tooling decisively instead of half-supporting both paths for months

## Risk: performance collapses on large maps

Mitigation:

- chunked meshes
- instancing for repeated props
- measured benchmarks during Phase 1 and Phase 2
- avoid one-node-per-tile architecture

## Definition of Done

The migration is complete when:

- the main Godot client is fully 3D
- no required gameplay path depends on sprite maps
- the content editor no longer exposes sprite workflows or sprite settings
- validators enforce 3D asset references instead of sprite coordinates
- the old `sprites/` manifests are deleted or fully retired
- core gameplay is playable and readable at fortress scale

## Recommended First Sprint

1. Build a 3D spike scene with blockout floors, walls, one dwarf, and raycast tile picking.
2. Define the 3D visual metadata schema and runtime registry shape.
3. Remove sprite dependencies from the migration target list and write a concrete deletion checklist for the content editor.
4. Decide whether the editor gets a replacement 3D asset catalog or no visual asset editor at all.
5. Build the first terrain and prop kit before committing to large-scale asset production.

## Bottom Line

This migration is feasible because the simulation is already separated from the client. The hard parts are not the game rules; they are:

- replacing the 2D render/input stack
- defining a maintainable 3D asset contract
- controlling art scope
- removing sprite tooling cleanly instead of carrying it forever

If you keep the simulation boundary intact and avoid demanding a unique handcrafted model for every current visual entry, you can reach a first playable 3D build without rewriting the whole project.