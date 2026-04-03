# Persistence Architecture Plan

## Goal

Support both kinds of persistence the game needs:

1. Fortress persistence: save and load the live simulation state of one active fortress.
2. World persistence: keep a generated world, its macro history, and its mutable cross-fort state so players can retire, reclaim, and start multiple forts in the same world.

The architecture should preserve DF-style world identity without duplicating large immutable worldgen payloads into every fortress save.

## Current state

The current save path is fortress-only and monolithic:

- `SaveSystem` writes a single JSON blob per slot through `GameSimulation.Save()`.
- `SaveGameSystem` is only a thin wrapper around full-simulation save/load.
- `MapGenerationService` caches generated world, history, region, and local data in memory only.
- `MapGenerationService.OnSave()` persists only the last embark context, not the world package.
- Many GameLogic systems still have no-op `OnSave()` / `OnLoad()` methods, so long-lived simulation state is incomplete.

This is good enough for a single-session prototype but not for a DF-like world lifecycle.

## Design principles

1. Do not duplicate immutable worldgen data into every fortress save.
2. Keep immutable generated baseline separate from mutable world deltas.
3. Fortress saves should reference a world package by stable ID.
4. Local fortress mutations must survive fort retirement and later reclaim.
5. Save format must support schema versioning and partial upgrades.
6. Runtime code should not depend on cache residency; persisted world data must reload deterministically.

## Persistence split

### World package

Stores data that belongs to the world as a whole.

Contents:

- world metadata: seed, generation settings, version, created time
- generated world map baseline
- generated history baseline
- generated region cache entries that have been materialized
- generated local/embark baseline entries that have been materialized
- mutable world-level deltas:
  - territory changes
  - historical event append log
  - retired fortress registry
  - site state changes
  - region discovery / visited markers
- references to fortress saves that belong to the world

### Fortress package

Stores one active or retired fortress simulation.

Contents:

- fortress metadata: fortress ID, world ID, embark coord, site ID, created/retired time
- full live GameSimulation save payload for fortress runtime systems
- local mutation overlay if the fortress modifies tiles/items/buildings beyond baseline worldgen
- fortress-specific query metadata such as name, status, retired/abandoned/reclaimed

## Proposed file layout

```text
saves/
  index.json
  worlds/
    <worldId>/
      world_meta.json
      world_map.json
      history.json
      world_delta.json
      regions/
        <worldX>_<worldY>.json
      locals/
        <worldX>_<worldY>__<regionX>_<regionY>.json
      forts.json
  fortresses/
    <fortressId>/
      meta.json
      simulation.json
      local_delta.json
```

The important split is semantic, not the exact filenames.

## Runtime ownership model

### Immutable baseline

Owned by world persistence:

- `GeneratedWorldMap`
- `GeneratedWorldHistory`
- generated `GeneratedRegionMap` and `GeneratedEmbarkMap` baselines

### Mutable world state

Owned by a new world persistence service:

- changes to macro history after fortress start
- site ownership changes
- retired fortress records
- discovered / settled / ruined site state
- local world overlays that should survive across fortresses

### Mutable fortress state

Owned by the existing simulation save path:

- entities
- items
- buildings
- jobs
- needs, mood, combat, fluids, vegetation, announcements
- fortress-local tile state and stockpiles

## Required services

### `WorldPersistenceService`

New GameLogic service responsible for:

- creating world IDs and fortress IDs
- saving/loading world packages
- loading generated baseline plus mutable world delta
- saving/loading local embark overlays
- resolving world references for fortress saves

### `FortressPersistenceService`

Can remain a thin layer over the current simulation save path initially, but should grow to:

- write fortress metadata envelope
- serialize fortress-local simulation payload
- merge local map overlay back into world package on retire/reclaim

## Save envelope format

Every persisted root file should carry:

- `schemaVersion`
- `contentType`
- `worldId`
- `fortressId` when applicable
- `createdUtc`
- `updatedUtc`

This allows forward migration and clear ownership.

## Implementation phases

## Phase 1: Wrap the current save path

Objective:

- Keep current `GameSimulation.Save()` working.
- Add a small metadata envelope around fortress saves.

Changes:

- extend `SaveSystem` to write `meta.json` plus `simulation.json`
- add stable `worldId` and `fortressId`
- persist `GeneratedEmbarkContext` reference with the fortress

Acceptance:

- loading a fortress save restores the same active fort as today
- save files are versioned and reference a world ID

## Phase 2: Persist world packages

Objective:

- Save generated world and history independently from active fortress state.

Changes:

- add `WorldPersistenceService`
- serialize `GeneratedWorldMap` and `GeneratedWorldHistory`
- move `MapGenerationService` from memory-only caches to load-through world storage

Acceptance:

- a generated world can be re-opened without regenerating from seed
- multiple fortress saves can point at the same world ID

## Phase 3: Split local baseline from local delta

Objective:

- Avoid duplicating the entire embark baseline into each fortress save.

Changes:

- persist generated local map baseline once per embark
- record fortress mutations as overlay deltas
- add merge/apply logic when loading a fort on top of baseline

Acceptance:

- reclaiming a site restores the locally modified map
- unchanged local tiles are not duplicated into every fortress save

## Phase 4: Retire and reclaim forts

Objective:

- Support DF-style world continuity.

Changes:

- add fortress lifecycle states: active, retired, fallen, reclaimed
- persist retired fort metadata in the world package
- add APIs to start a new fort in an existing world or reclaim an old site

Acceptance:

- player can retire a fortress and start another in the same world
- reclaim loads the modified local state of the prior site

## Phase 5: World evolution after fortress start

Objective:

- Keep world state alive outside one active fortress.

Changes:

- persist post-start world events and site deltas
- append runtime history events into world-level storage
- let future fortress starts consume updated history rather than only generated history

Acceptance:

- world package reflects persistent consequences of forts and macro events

## Code areas to change

- `src/DwarfFortress.GameLogic/Systems/SaveSystem.cs`
- `src/DwarfFortress.GameLogic/Systems/SaveGameSystem.cs`
- `src/DwarfFortress.GameLogic/World/MapGenerationService.cs`
- `src/DwarfFortress.GameLogic/World/WorldHistoryRuntimeService.cs`
- `src/DwarfFortress.GameLogic/Core/GameSimulation.cs`
- `src/DwarfFortress.GameLogic/GameBootstrapper.cs`
- new `WorldPersistenceService` and fortress metadata models

## Testing strategy

1. Save/load fortress round-trip preserves current simulation behavior.
2. World package round-trip preserves generated world/history fingerprints.
3. Two fortress saves can reference one world package without duplication bugs.
4. Local delta overlay survives retire and reclaim.
5. Schema version mismatch yields controlled migration or a clear failure.

## Recommended execution order

1. Fortress save envelope.
2. World package persistence.
3. Local baseline/delta split.
4. Retire/reclaim workflow.
5. Post-start world mutation persistence.

## What not to do

- Do not keep saving everything into one giant file forever.
- Do not make world persistence depend on `MapGenerationService` cache residency.
- Do not duplicate immutable world/history JSON into every fortress slot.
- Do not let retired-fort world state live only in client-side files or UI metadata.