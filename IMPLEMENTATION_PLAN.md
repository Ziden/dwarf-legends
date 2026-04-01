# Implementation Plan — Dwarf Fortress Clone
### Execution Order: Bottom-Up, Test-First

> Every phase produces **compilable, tested code** before the next phase begins.  
> No phase touches the Godot layer until all GameLogic phases are solid.

---

## Solution Layout

```
c:\Users\gabri\Desktop\godot\DF\
├── PLAN.md
├── ARCHITECTURE.md
├── IMPLEMENTATION_PLAN.md          ← this file
├── src/
│   ├── DwarfFortress.sln
│   ├── DwarfFortress.GameLogic/    ← pure .NET 8 class library
│   ├── DwarfFortress.Godot/        ← Godot 4 project (added LAST)
│   └── DwarfFortress.Tests/        ← xUnit test project
└── data/                           ← JSON content definitions
    ├── materials.json
    ├── tiles.json
    ├── items.json
    ├── creatures.json
    ├── recipes.json
    ├── jobs.json
    ├── reactions.json
    └── world_events.json
```

---

## Phase 0 — Tooling & Solution Setup

- [x] Create `DwarfFortress.sln`
- [x] Create `DwarfFortress.GameLogic.csproj` (net8.0 class library, no Godot SDK)
- [x] Create `DwarfFortress.Tests.csproj` (xUnit, references GameLogic only)
- [x] Create starter `data/` JSON files with minimal content (enough for tests)
- [ ] CI: `dotnet build` and `dotnet test` both green

---

## Phase 1 — Core Infrastructure (GameLogic/Core/)

**Goal:** All wiring contracts defined. Zero game logic yet.

### Files to create:
- `Core/IGameSystem.cs`         — system contract (Initialize, Tick, UpdateOrder)
- `Core/ILogger.cs`             — logging interface
- `Core/IDataSource.cs`         — file-reading interface
- `Core/Vec3i.cs`               — engine-agnostic 3D integer vector
- `Core/Vec2i.cs`               — engine-agnostic 2D integer vector
- `Core/EventBus.cs`            — typed C# delegate event bus
- `Core/Commands.cs`            — all ICommand record types
- `Core/CommandDispatcher.cs`   — type → handler routing
- `Core/GameContext.cs`         — passed to systems during Initialize
- `Core/GameSimulation.cs`      — top-level entry point; owns EventBus + systems
- `Core/SystemIds.cs`           — string constants for all system IDs (no magic strings)
- `Core/EventIds.cs`            — string constants (where applicable)

### Tests to write (Phase1Tests/):
- `EventBusTests.cs`            — subscribe, emit, multiple subscribers, no cross-contamination
- `CommandDispatcherTests.cs`   — register handler, dispatch, unknown command no-op
- `Vec3iTests.cs`               — arithmetic, Neighbours6, equality
- `GameSimulationTests.cs`      — RegisterSystem, Initialize order, Tick order

---

## Phase 2 — Data / Definition Layer (GameLogic/Data/)

**Goal:** JSON definitions load and resolve correctly. Registries are type-safe.

### Files to create:
- `Data/Registry.cs`            — `Registry<T>` generic typed read-only registry
- `Data/TagSet.cs`              — immutable set of string tags; HasAll/HasAny
- `Data/EffectBlock.cs`         — data record: { Op, Params }
- `Data/Modifier.cs`            — data record: { SourceId, Type, Value, Duration }
- `Data/ModifierStack.cs`       — flat+pctAdd+pctMult resolution
- `Data/SaveWriter.cs`          — for OnSave contract (simple JSON wrapper)
- `Data/SaveReader.cs`          — for OnLoad contract
- `Data/DataManager.cs`         — IGameSystem; loads all JSON via IDataSource
- **Definitions (Data/Defs/):**
  - `MaterialDef.cs`
  - `TileDef.cs`
  - `ItemDef.cs`
  - `CreatureDef.cs`
  - `RecipeDef.cs`
  - `JobDef.cs`
  - `ReactionDef.cs`
  - `WorldEventDef.cs`
  - `BuildingDef.cs`
  - `BiomeDef.cs`
- **JSON content (data/):**
  - `materials.json`  — stone, wood, iron, copper, gold, coal, food, drink
  - `tiles.json`      — floor, wall, ramp, empty, water, magma, tree
  - `items.json`      — log, plank, rock, ore, bar, food_chunk, drink_flask, bed, door
  - `recipes.json`    — carpentry, smelting, cooking, brewing, smithing
  - `jobs.json`       — mine, cut_tree, haul, build, craft, eat, drink, sleep, train
  - `creatures.json`  — dwarf, goblin, troll, elk, carp (RIP)
  - `reactions.json`  — magma+water → obsidian+steam, wood+fire → ash+smoke
  - `world_events.json` — migrant_wave, siege, caravan, megabeast_attack

### Tests to write (Phase2Tests/):
- `RegistryTests.cs`            — Add, Get, GetOrNull, duplicate detection
- `TagSetTests.cs`              — HasAll, HasAny, Contains, immutability
- `ModifierStackTests.cs`       — flat, pctAdd, pctMult, Remove by sourceId, stacking
- `DataManagerTests.cs`         — loads JSON, registers defs, returns correct defs
- `RecipeDefTests.cs`           — input tag matching, output generation rules

---

## Phase 3 — World & Map Layer (GameLogic/World/)

**Goal:** Fully navigable 3D tile world. Chunk-based. Pathfinding working.

### Files to create:
- `World/TileData.cs`           — runtime tile struct: type, material, fluid, designation flags
- `World/TileFlags.cs`          — bit flags: IsPassable, IsOpaque, IsDesignated, IsConstructed
- `World/TileTrait.cs`          — ITileTrait interface + registry (data-driven tile behaviours)
- `World/Chunk.cs`              — 16×16×4 tile array + dirty flag
- `World/WorldMap.cs`           — IGameSystem; owns all Chunks; SetTile/GetTile; emits TileChangedEvent
- `World/WorldGenerator.cs`     — layered-noise world gen; produces WorldMap
- `World/ZLevelManager.cs`      — tracks current view z-level; emits ZLevelChangedEvent
- `World/Pathfinder.cs`         — 3D A* with IPassable check from WorldMap

### Tests to write (Phase3Tests/):
- `TileDataTests.cs`            — default values, IsPassable rules
- `ChunkTests.cs`               — get/set, bounds, dirty tracking
- `WorldMapTests.cs`            — SetTile fires TileChangedEvent, GetTile/SetTile round-trip
- `PathfinderTests.cs`          — straight path, detour around wall, no-path returns empty

---

## Phase 4 — Entity Layer (GameLogic/Entities/)

**Goal:** Dwarves and creatures exist, have components, live and die.

### Files to create:
- `Entities/Entity.cs`          — abstract base: Id, DefId, IsAlive, ComponentBag
- `Entities/EntityRegistry.cs`  — IGameSystem; all live entities; NextId(); GetById; KillEntity
- `Entities/ComponentBag.cs`    — `Get<T>()`, `TryGet<T>()`, `Add<T>()`, `Has<T>()`
- **Components (Entities/Components/):**
  - `PositionComponent.cs`      — Vec3i, facing
  - `StatComponent.cs`          — base stats + ModifierStack per stat
  - `SkillComponent.cs`         — skill levels, XP
  - `NeedsComponent.cs`         — hunger, thirst, sleep, social, recreation levels
  - `MoodComponent.cs`          — current Mood enum, threshold breaks
  - `ThoughtComponent.cs`       — list of active Thought records (positive + negative)
  - `InventoryComponent.cs`     — held items, worn items
  - `LaborComponent.cs`         — enabled labor flags
  - `HealthComponent.cs`        — body parts, wounds, bleeding
- **Concrete entities:**
  - `Entities/Dwarf.cs`         — Entity subclass; pre-wired components; Name
  - `Entities/Creature.cs`      — Entity subclass; AI-driven
  - `Entities/Item.cs`          — Entity subclass; MaterialId, quality, stackable
  - `Entities/Building.cs`      — Entity subclass; TileDef footprint, construction state

### Tests to write (Phase4Tests/):
- `EntityRegistryTests.cs`      — add, get, kill, NextId uniqueness, GetAlive
- `ComponentBagTests.cs`        — Add/Get/Has/TryGet, missing throws, double-add throws
- `StatComponentTests.cs`       — base value, modifiers apply correctly
- `NeedsComponentTests.cs`      — tick decay, full/empty clamping
- `ThoughtComponentTests.cs`    — add thought, expiry, happiness sum

---

## Phase 5 — Job System (GameLogic/Jobs/)

**Goal:** Dwarves pick up and execute jobs. Jobs complete or fail cleanly.

### Files to create:
- `Jobs/Job.cs`                 — data record: Id, DefId, TargetPos, Status enum, AssignedDwarfId
- `Jobs/JobStatus.cs`           — enum: Pending, Assigned, InProgress, Complete, Failed, Cancelled
- `Jobs/IJobStrategy.cs`        — strategy interface: CanExecute, GetSteps, OnInterrupt
- `Jobs/ActionStep.cs`          — MoveTo / WorkAt / HaulItem / Wait union
- `Jobs/JobSystem.cs`           — IGameSystem; queue; assign; tick; complete
- `Jobs/JobQueue.cs`            — priority queue; labor-filtered assignments
- **Strategies (Jobs/Strategies/):**
  - `MineTileStrategy.cs`
  - `CutTreeStrategy.cs`
  - `HaulItemStrategy.cs`
  - `ConstructBuildingStrategy.cs`
  - `CraftItemStrategy.cs`
  - `SleepStrategy.cs`
  - `EatStrategy.cs`
  - `DrinkStrategy.cs`
  - `IdleStrategy.cs`
- `Jobs/DesignationSystem.cs`   — IGameSystem; handles DesignateMineCommand → creates Jobs

### Tests to write (Phase5Tests/):
- `JobSystemTests.cs`           — create job, assign dwarf, tick to complete
- `JobQueueTests.cs`            — priority ordering, labor filter, capacity
- `DesignationSystemTests.cs`   — dispatch command, jobs created for each tile
- `MineTileStrategyTests.cs`    — steps generated, tile changes on completion, boulder spawned

---

## Phase 6 — Needs, Moods & Thoughts (GameLogic/Systems/)

**Goal:** Dwarves have needs, get thoughts, enter moods, can have tantrums / depression.

### Files to create:
- `Systems/NeedsSystem.cs`      — IGameSystem; decrement needs each tick; emit NeedCriticalEvent
- `Systems/ThoughtSystem.cs`    — IGameSystem; listen events → add thoughts; expire old thoughts
- `Systems/MoodSystem.cs`       — IGameSystem; sum thoughts → mood level; emit MoodChangedEvent
- `Systems/SkillSystem.cs`      — IGameSystem; listen job events → award XP → level up
- `Systems/HealthSystem.cs`     — IGameSystem; wounds, bleeding, healing
- `Systems/TimeSystem.cs`       — IGameSystem; tracks world time (year/month/day/hour); seasons

### Tests to write (Phase6Tests/):
- `NeedsSystemTests.cs`         — tick reduces hunger, critical threshold fires event
- `MoodSystemTests.cs`          — positive thoughts raise mood, negative lower, threshold crossings
- `ThoughtSystemTests.cs`       — thought added on correct event, expires after duration
- `SkillSystemTests.cs`         — job complete adds XP, XP threshold triggers level
- `TimeSystemTests.cs`          — tick advances time, season changes fire events

---

## Phase 7 — Production, Items & Stockpiles (GameLogic/Systems/)

**Goal:** Dwarves can mine, smelt, craft. Items exist. Stockpiles hold them.

### Files to create:
- `Systems/ItemSystem.cs`       — IGameSystem; create/destroy items; GetAt; emits ItemCreatedEvent
- `Systems/StockpileManager.cs` — IGameSystem; zones → filter rules; nearest available item
- `Systems/RecipeSystem.cs`     — IGameSystem; production orders, input reservation, output generation
- `Systems/EffectApplicator.cs` — IGameSystem; op → handler registry (initialized with all effect types)
- `Systems/ReactionPipeline.cs` — IGameSystem; TileChangedEvent → check ReactionDefs → fire effects
- `Systems/ModifierSystem.cs`   — IGameSystem; handles add_modifier effect; ticks down durations

### Tests to write (Phase7Tests/):
- `ItemSystemTests.cs`          — create item, find item at pos, destroy item
- `StockpileManagerTests.cs`    — zone accepts/rejects by tag, nearest haul path
- `RecipeSystemTests.cs`        — order queued, inputs reserved, output created on complete
- `ReactionPipelineTests.cs`    — magma+water → obsidian, steam spawned

---

## Phase 8 — World Generation (GameLogic/World/)

**Goal:** A playable embark site can be generated from a seed.

### Files to create:
- `World/NoiseGenerator.cs`     — simplex noise wrapper (pure C#, no Godot)
- `World/WorldGenConfig.cs`     — embark settings: size, depth, biome, seed
- `World/LayeredWorldGen.cs`    — surface topology layer, stone layers, ore veins, water table, caverns

### Tests to write (Phase8Tests/):
- `WorldGenTests.cs`            — same seed produces identical map
- `WorldGenTests.cs`            — generated map has surface, subterranean layers, ores present
- `WorldGenTests.cs`            — embark site has at least one fresh water source

---

## Phase 9 — Fluid Simulation (GameLogic/Systems/)

**Goal:** Water flows, spreads, drowns dwarves. Magma too.

### Files to create:
- `Systems/FluidSimulator.cs`   — IGameSystem; cellular automata per tick; pressure levels 0–7

### Tests to write (Phase9Tests/):
- `FluidSimulatorTests.cs`      — water spreads to adjacent lower tiles, respects walls
- `FluidSimulatorTests.cs`      — full tile blocks creature movement
- `FluidSimulatorTests.cs`      — magma+water → obsidian via ReactionPipeline

---

## Phase 10 — Military & Combat (GameLogic/Systems/)

**Goal:** Squads can be formed, equipped, deployed, and fight.

### Files to create:
- `Systems/MilitaryManager.cs`  — IGameSystem; squads, schedules, CreateSquadCommand
- `Systems/CombatSystem.cs`     — IGameSystem; attack resolution, body part targeting, wounds
- `Entities/Components/MilitaryComponent.cs` — squad membership, alert status, uniform

### Tests to write (Phase10Tests/):
- `CombatSystemTests.cs`        — attack hits, damage applied, death event fired
- `MilitaryManagerTests.cs`     — squad created, dwarf assigned, alert toggles

---

## Phase 11 — World Events (GameLogic/Systems/)

**Goal:** Migrants arrive, caravans visit, sieges happen, megabeasts appear.

### Files to create:
- `Systems/WorldEventManager.cs` — IGameSystem; time-based triggers; spawn events from WorldEventDefs

### Tests to write (Phase11Tests/):
- `WorldEventManagerTests.cs`   — migrant wave fires after year 1, siege fires at year 2+

---

## Phase 12 — Snapshot System (GameLogic/Snapshots/)

**Goal:** All render-facing data leaves simulation as read-only records.

### Files to create:
- `Snapshots/DwarfSnapshot.cs`
- `Snapshots/TileViewSnapshot.cs`
- `Snapshots/ChunkViewSnapshot.cs`
- `Snapshots/AnnouncementSnapshot.cs`
- `Snapshots/SnapshotSystem.cs`  — IGameSystem; builds and emits snapshots each tick

### Tests to write (Phase12Tests/):
- `SnapshotSystemTests.cs`       — snapshot reflects latest tile state, dwarf state

---

## Phase 13 — Save / Load (GameLogic/Systems/)

**Goal:** Game state serializes and deserializes correctly.

### Files to create:
- `Systems/SaveSystem.cs`        — IGameSystem; coordinates OnSave/OnLoad across all systems
- `Data/SaveWriter.cs`           — thin wrapper over System.Text.Json
- `Data/SaveReader.cs`           — thin wrapper over System.Text.Json

### Tests to write (Phase13Tests/):
- `SaveSystemTests.cs`           — save → load → same tile map, same dwarf state

---

## Phase 14 — Godot Bridge (DwarfFortress.Godot/)

**Goal:** Rendering and input wired to running GameSimulation.

### Files to create (Godot project):
- `Bridge/GodotLogger.cs`
- `Bridge/GodotDataSource.cs`
- `Bootstrap/GameBootstrapper.cs`
- `Bridge/GodotEventBridge.cs`
- `Rendering/WorldRenderer.cs`    — ChunkViewSnapshot → TileMapLayer
- `Rendering/EntityRenderer.cs`   — DwarfSnapshot → sprite/label
- `UI/HUDController.cs`
- `UI/DwarfPanel.cs`
- `UI/AnnouncementLog.cs`
- `UI/DesignationToolbar.cs`
- `UI/StockpileConfigUI.cs`
- `UI/WorkshopOrderUI.cs`

*(No xUnit tests for the Godot layer — tested via manual play + GDUnit if desired)*

---

## Execution Status

| Phase | Status | Notes |
|---|---|---|
| 0 — Solution Setup | ✅ | |
| 1 — Core Infrastructure | ✅ | |
| 2 — Data Definitions | ✅ | |
| 3 — World/Map | ✅ | |
| 4 — Entities | ✅ | |
| 5 — Job System | ✅ | |
| 6 — Needs/Moods | ✅ | |
| 7 — Items/Stockpiles | ✅ | |
| 8 — World Gen | ✅ | |
| 9 — Fluid Sim | ✅ | |
| 10 — Military/Combat | ✅ | |
| 11 — World Events | ✅ | |
| 12 — Snapshots | ✅ | |
| 13 — Save/Load | ✅ | |
| 14 — Godot Bridge | 🔲 | Requires Godot project setup |

---

*Implementation plan — March 2026*
