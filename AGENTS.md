# AGENTS.md

## Project goal

The core goal of this project is depth: story, objects, behaviors, simulation, and interactions that compose into emergent outcomes.

Agents working in this repository should optimize for:

- Deep systemic gameplay over shallow feature count.
- Root-cause fixes over patches, hacks, and special cases.
- Simple code, clear contracts, and low coupling.
- Data-driven extensibility wherever possible.
- Good tests and repeatable validation.
- Performance awareness in hot paths without premature over-engineering.

## Non-negotiable engineering rules

- Keep code simple. Follow KISS, clean code, and SOLID.
- Fix problems at the root cause, even when the correct fix is slightly larger.
- Do not add dirty workarounds unless the user explicitly asks for a temporary stopgap.
- Avoid hard-coded strings for IDs, tags, materials, jobs, buildings, and tile defs.
- Prefer shared constants and helper types over string literals.
- Preserve deterministic behavior where systems depend on stable ordering or seeded randomness.
- Add or update tests when changing simulation behavior.
- Do not couple GameLogic to Godot.

## Solution map

- `src/DwarfFortress.GameLogic`
  - Pure .NET simulation core.
  - No Godot dependencies.
  - Contains world state, entities, systems, jobs, data defs, and query models.
- `src/DwarfFortress.GodotClient`
  - Godot 4 client.
  - Boots GameLogic, renders the world, and forwards player input.
- `src/DwarfFortress.Tests`
  - Main simulation and gameplay tests.
- `src/DwarfFortress.WorldGen`
  - World generation pipeline and supporting content loaders.
- `src/DwarfFortress.WorldGen.Tests`
  - Worldgen-focused tests.
- `src/DwarfFortress.ContentEditor`
  - Content and map preview tooling.

## Architectural expectations

### Simulation boundaries

- `DwarfFortress.GameLogic` is engine-agnostic and must stay that way.
- GameLogic should use plain .NET types and internal abstractions, not Godot classes.
- The client is a wrapper around simulation state, not the owner of game rules.

### Data-driven design

- Prefer adding content through JSON definitions instead of new code paths.
- New content should slot into registries, tags, and existing system hooks whenever possible.
- Definitions are immutable content. Runtime instances carry mutable state.

### Event-driven composition

- Systems should communicate through the event bus, not by grabbing each other directly unless they are reading stable query/state APIs.
- Cross-system behavior should emerge from event chains and shared state, not bespoke per-feature glue.

## Runtime source of truth

At runtime, client code and simulation-facing features should read live systems, not stale snapshots.

Prefer these systems as the main query surface:

- `WorldMap`
- `EntityRegistry`
- `ItemSystem`
- `BuildingSystem`
- `StockpileManager`
- `SpatialIndexSystem`
- `WorldQuerySystem`

Important rules:

- Tile-scoped client interactions should go through `WorldQuerySystem.QueryTile(Vec3i)`.
- UI and inspector code should prefer `WorldQuerySystem` and `WorldQueryModels` DTOs over ad hoc scans.
- Per-entity recent activity should come from `EntityEventLogSystem` via query models, not direct EventBus subscriptions in UI.
- Fortress-wide notifications come from `FortressAnnouncementSystem` in GameLogic and should be rendered from the shared query stream, not emitted ad hoc in the client.

## ID and tag conventions

- Do not compare tags or definition IDs with raw string literals.
- Use shared constants such as:
  - `TagIds`
  - `MaterialIds`
  - `TileDefIds`
  - `ItemDefIds`
  - `BuildingDefIds`
  - other existing `*Ids` helpers in GameLogic
- Use `CreatureDefTagExtensions` for creature semantics such as aquatic, swimmer, hostile, and groomer instead of duplicating tag lists.
- Stockpile filters intentionally mix material IDs and item tags. Use typed constants for both.
- Rock flavor should come from `MaterialId` on shared stone wall/floor definitions, not separate per-rock structural tile defs.

## Behavior and movement invariants

These rules matter because several systems depend on deterministic movement and synchronized spatial state.

- `WanderBehavior` is creature-only. Dwarves should move through jobs and idle-job logic, not autonomous wandering.
- If a system moves an entity directly, it must respect traversal rules and emit `EntityMovedEvent`.
- If movement occurs without the event, `SpatialIndexSystem` and `WorldQuerySystem` will drift from actual entity state.
- Cached job paths can go stale when entities are displaced. Movement logic must validate adjacency before consuming queued steps and reroute from the current position when needed.
- Flee behavior should advance one step at a time and emit ordinary movement events. Do not teleport to the end of a flee path.
- Work steps should be tied to an explicit required tile. If a dwarf is displaced mid-work, the job should restore a move step and reset progress rather than completing from the wrong position.

## Godot client guidance

- The Godot client lives in `src/DwarfFortress.GodotClient` and boots GameLogic through the bootstrapper.
- The client reads JSON content from the GameLogic data folder through `FolderDataSource`.
- Main playable scene is `Scenes/Main.tscn`.
- Main render/simulation entry point is `Scripts/GameRoot.cs`.

Rendering and UI rules:

- Treat the client tile cache as a render cache, never as authoritative world state.
- Keep viewport-bounded caching and incremental invalidation behavior intact. Avoid reintroducing full visible-cache rebuilds every frame.
- Tree rendering is an overlay over inferred ground, not an opaque base tile.
- Shared tile composition belongs in `Scripts/TileRenderHelper.cs` so render changes apply consistently across views.
- Tile and liquid adjacency smoothing should go through the shared smoothing helpers, not custom neighbor probe code.
- Dwarf visuals should render from `DwarfAppearanceComponent` state, not a single shared texture.
- Labor feedback in the client is intentionally lightweight and pulse-based. Prefer extending that system over adding many extra nodes.

## Worldgen and content notes

- Worldgen geology, mineral veins, wildlife tables, biome presets, and tree species are data-driven through `data/ConfigBundle/worldgen/worldgen_content.json` plus related content files.
- If a new geology, plant, wildlife, or tree configuration fits the current schema, add it through data rather than code.
- The worldgen registries are thin adapters over shared content catalogs. Avoid adding new hard-coded switch tables when existing catalogs can carry the variation.
- Ground-plant and canopy-plant selection should remain aligned with the shared worldgen content catalogs.

## Performance guidance

Performance matters because the simulation is expected to scale to many entities.

- Watch hot loops in simulation systems first, especially behavior, world scans, fluid, stockpiles, and vegetation.
- Prefer event-driven tracking over full-world rescans when the world can tell us what changed.
- `WorldMap.SetTile` and `EventBus.Emit` are synchronous on the simulation thread. Do not introduce background mutation work unless the mutation model changes.
- Background work should stay limited to safe read-only or isolated tasks.
- Profiling seam is `GameSimulation.Tick`, available through `GameContext.Profiler`.
- The Godot client already exposes simulation profiler data through the debug UI and custom monitors. Reuse that path before inventing another profiler bridge.

Known useful examples:

- Vegetation and harvest systems should prefer tracked candidates over periodic full-surface sweeps.
- Behavior changes should avoid LINQ-heavy or allocation-heavy work in per-entity hot paths.
- Query systems and spatial indexes should be used before broad registry scans.

## Testing and validation

Before closing work, validate the narrowest relevant slice first, then broaden if needed.

Useful commands:

- `dotnet test .\src\DwarfFortress.Tests\DwarfFortress.Tests.csproj`
- `dotnet test .\src\DwarfFortress.WorldGen.Tests\DwarfFortress.WorldGen.Tests.csproj`
- `dotnet test .\src\DwarfFortress.Tests\DwarfFortress.Tests.csproj --filter <NameFragment>`
- `dotnet build .\src\DwarfFortress.GodotClient\DwarfFortress.GodotClient.csproj`
- `dotnet build .\src\DwarfFortress.sln`

Validation expectations:

- If changing GameLogic behavior, run focused simulation tests.
- If changing rendering or client boot flow, build the Godot client and, when practical, launch the main scene.
- If changing content tooling or Razor code, build the ContentEditor path that your change touches.
- Do not claim a broad green status unless you actually ran the broader build or test command.

## Agent workflow for changes

When making changes in this repository:

1. Read the surrounding system and its neighboring systems before editing.
2. Check for an existing shared helper, constant, registry, or query path before adding new logic.
3. Prefer minimal, focused edits that preserve current architecture.
4. Update tests when behavior changes.
5. Validate the exact slice affected by the change.

For reviews and debugging, pay extra attention to:

- raw string ID comparisons
- silent client-only state that should live in GameLogic
- direct position writes without movement events
- full-map rescans inside per-tick systems
- snapshot duplication when a live query system already exists
- new Godot dependencies leaking into GameLogic

## What good changes look like here

- A new feature is added through data plus small extension seams, not by branching many central systems.
- A bug fix removes the wrong assumption instead of layering another exception.
- A UI improvement uses existing query models and shared rendering helpers.
- A performance fix reduces repeated searches or allocations without changing gameplay semantics.
- A gameplay rule is enforced in the simulation layer, then surfaced consistently to the client.