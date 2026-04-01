# Dwarf Fortress Clone — Fortress Mode
### Godot 4 + C# Implementation Plan

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Technology Stack](#2-technology-stack)
3. [High-Level Architecture](#3-high-level-architecture)
4. [Project Folder Structure](#4-project-folder-structure)
5. [Core Systems](#5-core-systems)
   - 5.1 [Game Manager & Singletons](#51-game-manager--singletons)
   - 5.2 [Time System](#52-time-system)
   - 5.3 [Event Bus](#53-event-bus)
   - 5.4 [Save / Load System](#54-save--load-system)
6. [World & Map System](#6-world--map-system)
   - 6.1 [Tile Data Model](#61-tile-data-model)
   - 6.2 [Chunk System](#62-chunk-system)
   - 6.3 [World Generator](#63-world-generator)
   - 6.4 [Z-Level Manager](#64-z-level-manager)
   - 6.5 [Pathfinding](#65-pathfinding)
7. [Entity & Component Architecture](#7-entity--component-architecture)
   - 7.1 [Dwarf Entity](#71-dwarf-entity)
   - 7.2 [Dwarf Attributes & Skills](#72-dwarf-attributes--skills)
   - 7.3 [Needs & Moods](#73-needs--moods)
   - 7.4 [Thoughts System](#74-thoughts-system)
   - 7.5 [Relationships](#75-relationships)
8. [Job & Task System](#8-job--task-system)
   - 8.1 [Job Types](#81-job-types)
   - 8.2 [Job Queue & Priority](#82-job-queue--priority)
   - 8.3 [Task Assignment](#83-task-assignment)
   - 8.4 [Labor Designations](#84-labor-designations)
9. [Designation System](#9-designation-system)
10. [Buildings & Workshops](#10-buildings--workshops)
    - 10.1 [Construction Pipeline](#101-construction-pipeline)
    - 10.2 [Workshop Catalogue](#102-workshop-catalogue)
    - 10.3 [Production Orders](#103-production-orders)
11. [Stockpile System](#11-stockpile-system)
12. [Items & Inventory](#12-items--inventory)
    - 12.1 [Item Data Model](#121-item-data-model)
    - 12.2 [Materials & Quality](#122-materials--quality)
13. [Resource & Production Chains](#13-resource--production-chains)
    - 13.1 [Mining & Stone](#131-mining--stone)
    - 13.2 [Woodcutting & Carpentry](#132-woodcutting--carpentry)
    - 13.3 [Farming & Food Chain](#133-farming--food-chain)
    - 13.4 [Smelting & Metalworking](#134-smelting--metalworking)
    - 13.5 [Brewing & Cooking](#135-brewing--cooking)
    - 13.6 [Crafting & Luxury Goods](#136-crafting--luxury-goods)
14. [Room & Zone System](#14-room--zone-system)
26. [UI/UX Design Philosophy & Accessibility](#26-uiux-design-philosophy--accessibility)
    - 26.1 [Core Design Principles](#261-core-design-principles)
    - 26.2 [Visual Language & Readability](#262-visual-language--readability)
    - 26.3 [Onboarding & Tutorial System](#263-onboarding--tutorial-system)
    - 26.4 [Smart Notifications & Proactive Guidance](#264-smart-notifications--proactive-guidance)
    - 26.5 [Context-Sensitive Interaction](#265-context-sensitive-interaction)
    - 26.6 [Reworked Labor Management](#266-reworked-labor-management)
    - 26.7 [Reworked Stockpile Management](#267-reworked-stockpile-management)
    - 26.8 [Production Automation & Manager AI](#268-production-automation--manager-ai)
    - 26.9 [Map Navigation & Overview](#269-map-navigation--overview)
    - 26.10 [Input Design: Mouse-First with Keyboard Depth](#2610-input-design-mouse-first-with-keyboard-depth)
    - 26.11 [Pause-and-Plan Workflow](#2611-pause-and-plan-workflow)
    - 26.12 [Difficulty & Pacing Settings](#2612-difficulty--pacing-settings)
    - 26.13 [Accessibility Options](#2613-accessibility-options)
    - 26.14 [Quality-of-Life Features](#2614-quality-of-life-features)
15. [Military System](#15-military-system)
    - 15.1 [Squads & Scheduling](#151-squads--scheduling)
    - 15.2 [Combat](#152-combat)
    - 15.3 [Equipment & Uniform](#153-equipment--uniform)
    - 15.4 [Training & Skills](#154-training--skills)
16. [Creature & Wildlife AI](#16-creature--wildlife-ai)
17. [World Events](#17-world-events)
    - 17.1 [Seasons & Weather](#171-seasons--weather)
    - 17.2 [Migrants](#172-migrants)
    - 17.3 [Caravans & Trade](#173-caravans--trade)
    - 17.4 [Sieges & Raids](#174-sieges--raids)
    - 17.5 [Megabeasts & Wanderers](#175-megabeasts--wanderers)
18. [Fluid Simulation (Water & Magma)](#18-fluid-simulation-water--magma)
19. [Temperature & Fire System](#19-temperature--fire-system)
20. [Rendering & Visual Layer](#20-rendering--visual-layer)
    - 20.1 [Tile Renderer](#201-tile-renderer)
    - 20.2 [Z-Level Camera](#202-z-level-camera)
    - 20.3 [Lighting](#203-lighting)
21. [User Interface](#21-user-interface)
    - 21.1 [HUD Overview](#211-hud-overview)
    - 21.2 [Designation Toolbar](#212-designation-toolbar)
    - 21.3 [Dwarf Viewer](#213-dwarf-viewer)
    - 21.4 [Workshop & Production UI](#214-workshop--production-ui)
    - 21.5 [Stockpile Configuration](#215-stockpile-configuration)
    - 21.6 [Military Screen](#216-military-screen)
    - 21.7 [Announcements & Log](#217-announcements--log)
22. [Data-Driven Design (Resources & JSON)](#22-data-driven-design-resources--json)
23. [Performance Strategy](#23-performance-strategy)
24. [Implementation Phases / Roadmap](#24-implementation-phases--roadmap)
25. [Open Design Questions](#25-open-design-questions)

---

## 1. Project Overview

This document describes the complete plan for building a **Fortress Mode clone of Dwarf Fortress** using **Godot 4** and **C#** (Mono/.NET). The game is a colony-simulation / base-builder where the player oversees a group of dwarves digging out and developing an underground fortress. The player does not directly control dwarves — they issue high-level designations and orders, and the dwarves autonomously fulfil them.

**Core pillars:**
- Deep simulation of individual dwarf needs, personality, and relationships
- Emergent storytelling through interacting systems
- 3D tile-based world with Z-levels (2.5D view)
- Cascading resource and production chains
- Threats that escalate over time (raids, megabeasts, internal mental breakdowns)

---

## 2. Technology Stack

| Concern | Choice | Notes |
|---|---|---|
| Engine | Godot 4.x | Scene / Node system, built-in physics, signals |
| Language | C# (.NET 8) | Full OOP, collections, LINQ, async/await |
| Rendering | Godot TileMapLayer | One layer per Z-level; tile atlas per material group |
| Data storage | JSON resource files | All materials, recipes, creatures defined as data |
| Save format | JSON + GZip | Human-readable, compressible world state |
| Pathfinding | Custom 3D A* | Built on top of Godot AStar3D or custom grid |
| Noise | FastNoiseLite (built-in) | World generation |
| UI | Godot Control nodes | Mix of CanvasLayer HUD + docked panels |

---

## 3. High-Level Architecture

```
┌─────────────────────────────────────────────────┐
│                   GameManager                   │  (Autoload singleton)
│  TimeManager | EventBus | SaveSystem | Config   │
└────────────────────────┬────────────────────────┘
                         │
          ┌──────────────┼──────────────┐
          │              │              │
   ┌──────▼──────┐ ┌─────▼──────┐ ┌───▼──────────┐
   │  WorldSim   │ │ EntitySim  │ │   UIManager  │
   │  (Map/Tiles)│ │(Dwarves,   │ │  (HUD/Panels)│
   │  Fluids     │ │ Items,     │ └──────────────┘
   │  Weather    │ │ Creatures) │
   └──────┬──────┘ └─────┬──────┘
          │              │
   ┌──────▼──────┐ ┌─────▼──────┐
   │ JobSystem   │ │ EventSystem│
   │ (Queue,     │ │ (Raids,    │
   │  Assignment)│ │  Caravans, │
   └─────────────┘ │  Migrants) │
                   └────────────┘
```

Every major system is a **Godot Autoload Node** (singleton) or a **child manager node** owned by `GameManager`. Systems communicate via the **EventBus** (Godot signals) to avoid tight coupling.

---

## 4. Project Folder Structure

```
res://
├── addons/                    # Third-party plugins if needed
├── assets/
│   ├── tilesets/              # Tile atlases (stone, soil, ores, constructions…)
│   ├── sprites/               # Entity sprites, item icons
│   ├── ui/                    # UI theme, icons, fonts
│   └── audio/                 # SFX, ambient
├── data/                      # JSON data files (data-driven design)
│   ├── materials.json
│   ├── creatures.json
│   ├── items.json
│   ├── recipes.json
│   ├── workshops.json
│   ├── buildings.json
│   ├── traits.json
│   ├── skills.json
│   ├── jobs.json
│   └── biomes.json
├── scenes/
│   ├── main/
│   │   ├── Main.tscn          # Root scene
│   │   └── GameWorld.tscn
│   ├── world/
│   │   ├── WorldRenderer.tscn
│   │   ├── ZLevelLayer.tscn
│   │   └── Chunk.tscn
│   ├── entities/
│   │   ├── Dwarf.tscn
│   │   ├── Creature.tscn
│   │   └── ItemDrop.tscn
│   ├── buildings/
│   │   └── Workshop.tscn
│   └── ui/
│       ├── HUD.tscn
│       ├── DwarfPanel.tscn
│       ├── WorkshopPanel.tscn
│       ├── StockpilePanel.tscn
│       ├── MilitaryPanel.tscn
│       └── AnnouncementLog.tscn
├── scripts/
│   ├── autoloads/
│   │   ├── GameManager.cs
│   │   ├── TimeManager.cs
│   │   ├── EventBus.cs
│   │   ├── SaveSystem.cs
│   │   └── DataManager.cs
│   ├── world/
│   │   ├── WorldMap.cs
│   │   ├── Tile.cs
│   │   ├── Chunk.cs
│   │   ├── WorldGenerator.cs
│   │   ├── ZLevelManager.cs
│   │   ├── FluidSimulator.cs
│   │   └── Pathfinder.cs
│   ├── entities/
│   │   ├── Entity.cs            # Base class
│   │   ├── Dwarf.cs
│   │   ├── DwarfNeeds.cs
│   │   ├── DwarfMood.cs
│   │   ├── DwarfSkills.cs
│   │   ├── DwarfAttributes.cs
│   │   ├── DwarfThoughts.cs
│   │   ├── DwarfRelationships.cs
│   │   ├── Creature.cs
│   │   └── ItemDrop.cs
│   ├── jobs/
│   │   ├── JobSystem.cs
│   │   ├── Job.cs
│   │   ├── JobQueue.cs
│   │   └── jobs/               # One file per job type
│   │       ├── MineJob.cs
│   │       ├── HaulJob.cs
│   │       ├── ConstructJob.cs
│   │       ├── CraftJob.cs
│   │       ├── FarmJob.cs
│   │       └── ...
│   ├── production/
│   │   ├── Workshop.cs
│   │   ├── ProductionOrder.cs
│   │   ├── RecipeDatabase.cs
│   │   └── StockpileManager.cs
│   ├── military/
│   │   ├── MilitaryManager.cs
│   │   ├── Squad.cs
│   │   ├── CombatSystem.cs
│   │   └── EquipmentManager.cs
│   ├── events/
│   │   ├── WorldEventManager.cs
│   │   ├── MigrantWave.cs
│   │   ├── CaravanEvent.cs
│   │   ├── SiegeEvent.cs
│   │   └── MegabeastEvent.cs
│   ├── ui/
│   │   ├── HUDController.cs
│   │   ├── DwarfPanel.cs
│   │   ├── WorkshopPanel.cs
│   │   ├── StockpilePanel.cs
│   │   ├── MilitaryPanel.cs
│   │   └── AnnouncementLog.cs
│   └── rendering/
│       ├── WorldRenderer.cs
│       ├── ZLevelLayer.cs
│       └── EntityRenderer.cs
└── project.godot
```

---

## 5. Core Systems

### 5.1 Game Manager & Singletons

`GameManager` (Autoload) is the root orchestrator. It owns references to all major subsystem managers and handles the top-level game state machine:

```
States: MainMenu → WorldGen → Playing → Paused → GameOver
```

Responsibilities:
- Initialize all subsystems in order (DataManager first, then WorldMap, then Entities, then Jobs, then Events)
- Expose shared references so subsystems don't need `GetNode` paths
- Handle pause/unpause, which freezes `TimeManager` and halts job processing

### 5.2 Time System

`TimeManager` (Autoload) drives all simulation ticks.

- **Game tick rate:** Configurable; default ~20 ticks/second at normal speed
- **Speed levels:** Paused | ×1 | ×2 | ×5 | ×10 (emergency fast-forward)
- **Calendar:** Ticks → Hours → Days → Seasons (Spring/Summer/Autumn/Winter) → Years
  - 1 year = 4 seasons × 28 days = 112 days
  - 1 day = 24 hours of simulation time
- Emits signals: `OnTick`, `OnHourPassed`, `OnDayPassed`, `OnSeasonChanged`, `OnYearPassed`
- All simulation systems subscribe to `OnTick` and process their logic

### 5.3 Event Bus

`EventBus` (Autoload) is a central Godot signal hub. Every system emits and subscribes here instead of holding direct references.

Key signals (partial list):
```
TileChanged(Vector3I pos, TileData data)
JobCreated(Job job)
JobCompleted(Job job)
DwarfNeedCritical(Dwarf dwarf, NeedType need)
DwarfMoodChanged(Dwarf dwarf, Mood mood)
ItemCreated(Item item, Vector3I pos)
ItemHauled(Item item, Stockpile dest)
BuildingConstructed(Building b)
CaravanArrived(Caravan c)
SiegeBegun(Siege s)
AnnouncementPosted(string msg, Severity sev)
```

### 5.4 Save / Load System

`SaveSystem` (Autoload) serializes the full game state to JSON and compresses with GZip.

Save data includes:
- World map (tile data per chunk, stored as compressed bitfield where possible)
- All entity states (position, stats, inventory, relationships)
- All job queues
- Stockpile configurations
- Building / workshop states
- Production orders
- Military squads
- World event scheduler state
- Time/calendar state
- Fluid state

**Strategy:** Save in a background thread using `Task.Run` to avoid hitching. Auto-save every N in-game days (configurable).

---

## 6. World & Map System

### 6.1 Tile Data Model

Each world position `(x, y, z)` is a `TileData` struct:

```
struct TileData {
    TileType    Type          // Empty, Wall, Floor, Ramp, Stair, etc.
    MaterialId  Material      // Stone type, soil type, ore, wood, etc.
    byte        FluidLevel    // 0–7 (0 = dry, 7 = full)
    FluidType   Fluid         // None, Water, Magma
    byte        Temperature   // in abstract units
    bool        IsDesignated  // pending dig/build order
    bool        IsConstructed // player-built (not natural)
    RoomId      Room          // 0 if none; otherwise ID of room containing this tile
    byte        Fertility     // for farm plots
    byte        Light         // 0–255 ambient light value
}
```

Stored in a flat array per chunk for cache efficiency.

### 6.2 Chunk System

The world is divided into **chunks** of 16×16×4 tiles (x/y/z). This allows:
- Only loading/rendering visible chunks
- Efficient dirty-marking for re-render
- Manageable save file sizes

`WorldMap` owns a `Dictionary<Vector3I, Chunk>` where the key is chunk coordinates. Tiles are accessed via a global helper `WorldMap.GetTile(Vector3I worldPos)` which resolves chunk + local offset.

### 6.3 World Generator

`WorldGenerator` produces the starting embark site using layered noise:

**Generation passes (in order):**

1. **Heightmap** — FastNoiseLite (Fractal Brownian Motion) → determines where surface is
2. **Biome assignment** — temperature + moisture noise → forest, mountain, desert, tundra, etc.
3. **Rock layers** — stratified: soil/clay (top) → sedimentary → metamorphic → igneous → deep layer → magma sea
4. **Cave generation** — 3D Perlin worm algorithm → carves cave systems at depth
5. **Ore veins** — probability-based scatter per rock layer (magnetite in igneous, cassiterite in sedimentary, etc.)
6. **Aquifer layer** — optional layer of water-filled stone
7. **River / water features** — surface rivers cut through heightmap
8. **Trees & surface features** — scattered on surface tiles per biome
9. **Embark spawn placement** — find a valid flat area; place starting 7 dwarves + wagon

**Configurable embark parameters:** region size (min 48×48), depth (min 50 Z-levels), biome, mineral richness, hostile fauna.

### 6.4 Z-Level Manager

`ZLevelManager` tracks the currently viewed Z-level and manages rendering:

- Player scrolls up/down with hotkeys
- Only the current Z-level and tiles directly above/below are rendered
- Tiles above are shown semi-transparent to indicate overhangs
- Lighting adjusts based on distance from sky (Z-level above surface = full daylight; deep = zero ambient light)

### 6.5 Pathfinding

Custom 3D A* grid pathfinder (`Pathfinder.cs`):

- **Graph nodes:** Each passable tile is a node
- **Edges:** 6-directional movement (N/S/E/W/Up/Down) + diagonal (optional setting)
- **Special cases:**
  - Stairs (up/down links between Z-levels)
  - Ramps (diagonal Z movement)
  - Swimming / crossing water (cost penalty, requires swimmer skill at high levels)
  - Locked doors (faction-aware)
  - Temporary obstacles (other dwarves in narrow passages → wait/reroute)
- **Optimisations:**
  - Hierarchical pathfinding: macro-graph of regions for long-distance, fine A* for last segment
  - Path caching: reuse paths for dwarves on same route until map change invalidates cache
  - Async path requests via `Task.Run` so pathfinding never blocks the main thread
- **Connectivity check:** `FloodFill` utility to detect unreachable tiles before creating jobs (prevents dwarves from trying to reach walled-off areas)

---

## 7. Entity & Component Architecture

Entities use a **hybrid approach**: a base `Entity` C# class handles common concerns (position, move, inventory), with **specialised child classes** for Dwarf vs Creature vs Item. This avoids full ECS complexity while keeping code organised.

### 7.1 Dwarf Entity

`Dwarf : Entity` is the primary playable unit. Each dwarf is a Godot Node2D (sprite on the tile layer) backed by a rich C# object.

Key fields:
```
string Name
int Age
List<Trait> Personality       // cowardly, cheerful, stubborn, etc.
DwarfAttributes Attributes
DwarfSkills Skills
DwarfNeeds Needs
DwarfMood CurrentMood
List<Thought> Thoughts
List<Relationship> Relationships
Profession Profession
List<LaborType> EnabledLabors
Job CurrentJob
Item[] EquippedItems          // armour/weapons slots
List<Item> HeldItems          // carried during hauling
Vector3I Position
bool IsAlive
bool IsSleeping
Health Health
```

Dwarf AI loop (each tick):
1. Update Needs (decay hunger, thirst, sleep, rest, comfort, fun, social)
2. Update Mood from Thoughts accumulator
3. If in a **Mood Break** → override job with tantrum/berserk/catatonic logic
4. If current job is valid & reachable → continue it
5. Else → ask JobSystem for new job matching enabled labors
6. Execute one step of current job (move toward, perform action)

### 7.2 Dwarf Attributes & Skills

**Attributes** (physical/mental stats, partially set at birth, can grow slowly):

| Category | Attributes |
|---|---|
| Physical | Strength, Agility, Toughness, Endurance, Disease Resistance, Recovery Rate |
| Mental | Focus, Willpower, Creativity, Intuition, Patience, Memory, Linguistic Ability |
| Social | Empathy, Social Awareness |

**Skills** — over 40 skills, each with level 0–20 (Dabbling → Legendary+5):

Mining, Masonry, Carpentry, Woodcutting, Smithing, Weaponsmithing, Armorsmithing, Gem Cutting, Gem Setting, Leatherworking, Weaving, Clothesmaking, Cooking, Brewing, Farming (each plant type), Fishing, Animal Handling/Training, Surgery, Diagnosis, Bone Setting, Suturing, Wound Dressing, Ambusher, Fighter, Swordsman, Axeman, Hammerman, Spearman, Bowman, Dodge, Shield Use, Armor Use, Siege Engineering, Mechanics, Architecture, Engraving, Record Keeping, Appraiser, Consoling Dwarves, Judging Intent, Trade, Leadership, Teaching.

Skill usage: each time a dwarf performs a skilled action, they gain XP. XP thresholds raise skill level. Higher skill = faster work, better quality output, lower injury risk.

### 7.3 Needs & Moods

`DwarfNeeds` tracks an array of need values (0–100 scale, where 100 = fully satisfied):

| Need | Decays When | Satisfied By |
|---|---|---|
| Hunger | Always over time | Eating food |
| Thirst | Always over time | Drinking water or alcohol |
| Sleep | While awake | Sleeping in bed (bonus) or floor (penalty) |
| Rest | Strenuous work | Sitting/idle |
| Comfort | Poor quality furniture | Quality furniture, fine room |
| Fun / Recreation | Always slowly | Playing, reading, socialising |
| Social | Isolation | Talking to other dwarves |
| Safety | Threats nearby | No violence, no corpses, no vermin |

Each need has a `MoodImpact` function that converts current value into a mood modifier (−5 to +5 per need per tick).

### 7.4 Thoughts System

`DwarfThoughts` maintains a list of `Thought` records, each with a duration and a happiness modifier:

Positive examples: "had a fine meal", "slept in a grand bedroom", "became legendary miner", "a friend visited"
Negative examples: "was forced to sleep on the floor", "saw a corpse", "feeling crowded", "couldn't find food", "a friend died"

Each tick thoughts age and expire. The total `HappinessScore` is the sum of all active thought modifiers. The score drives `DwarfMood`.

### 7.5 Relationships

`DwarfRelationships` stores a dictionary of `EntityId → RelationshipRecord`:

```
struct RelationshipRecord {
    RelationshipType Type   // Acquaintance, Friend, BestFriend, Spouse, Parent, Child, Enemy, Rival
    int              Value  // −100 to +100
}
```

Interactions between dwarves (conversations, working together, witnessing trauma) update relationship values. High positive relationships generate positive thoughts; deaths of close relations cause major mood events.

---

## 8. Job & Task System

### 8.1 Job Types

Every action a dwarf can perform is a `Job`. Jobs are data objects, not scripts.

**Job categories:**

| Category | Examples |
|---|---|
| Mining | Dig tile, Smooth stone, Engrave |
| Woodcutting | Fell tree |
| Hauling | Move item to stockpile, Move item to workshop |
| Construction | Build wall, Build floor, Build ramp, Deconstruct |
| Farming | Till soil, Plant seeds, Harvest, Gather plants |
| Crafting | Any workshop production task |
| Caretaking | Feed incapacitated dwarf, Give water |
| Military | Train (individual), Patrol, Garrison |
| Medical | Diagnose, Treat, Operate |
| Social | Hold meeting, Console |
| Cleaning | Remove contaminant from tile, Dump refuse |
| Mechanic | Install trap/mechanism, Link mechanism |
| Animal | Butcher, Milk, Shear, Train, Capture |

Each `Job` object contains:
```
JobType   Type
Vector3I  TargetPos
EntityId  TargetEntity    // optional
ItemId    RequiredItem    // optional input material
int       TicksRequired   // base ticks to complete
int       TicksDone
int       Priority        // 1 (lowest) – 7 (emergency)
LaborType RequiredLabor
SkillType RelevantSkill
bool      IsReserved      // claimed by a dwarf
```

### 8.2 Job Queue & Priority

`JobQueue` is a sorted list of pending jobs. Sorting key: Priority (desc) → distance from idle dwarves (approx).

Special queues:
- **Hauling queue** — separate cheaply-assigned queue; any dwarf with hauling enabled claims the closest haul job
- **Emergency queue** — top-priority jobs (fire suppression, medical, military alerts) bypass normal queue

### 8.3 Task Assignment

`JobSystem` runs every N ticks (not every tick — reduces CPU load):

1. Collect all idle dwarves
2. For each idle dwarf, find the highest-priority job that:
   - Matches an enabled labor for that dwarf
   - Is reachable (connectivity check)
   - Is not already reserved
3. Assign job → dwarf marks job as reserved, begins pathfinding to target

If no job is found, dwarf enters idle behaviour (wander, socialise, seek recreation).

### 8.4 Labor Designations

Players toggle labors per dwarf via the Dwarf Panel. A `LaborType` enum covers all job categories. This is the primary control mechanism — not commanding individual dwarves.

**Labor profiles:** Predefined templates (Miner, Crafter, Soldier, Doctor, Peasant) can be applied in bulk.

---

## 9. Designation System

The player interacts with the world primarily through **designations** — marking tiles or zones for dwarves to act upon.

Types of designation:

| Designation | Tile Effect | Job Created |
|---|---|---|
| Mine | Wall → marked | MineJob |
| Chop Tree | Tree → marked | WoodcutJob |
| Gather Plants | Surface plant → marked | GatherJob |
| Smooth Stone | Floor/Wall → marked | SmoothJob |
| Engrave | Smoothed tile → marked | EngraveJob |
| Remove Designation | Clears marker | Cancels pending job |
| Dump Zone | Area marked as dump | Triggers haul-to-dump jobs for marked items |
| No-access zone / Burrow | Area restriction | Restricts dwarf pathing |

Designations are applied via **drag-box selection** in the UI. All tiles in the rectangle get the designation simultaneously, generating one job per tile.

---

## 10. Buildings & Workshops

### 10.1 Construction Pipeline

1. Player selects a building type from the Build menu
2. Player places the building footprint on the map (tiles highlighted)
3. System validates: tile must be appropriate (floor exists, not blocked, materials available nearby)
4. `ConstructJob` is created and added to the job queue
5. Dwarf claims the job, hauls required materials to site, then constructs
6. On completion: tiles are updated to `IsConstructed = true`, building entity is spawned, stockpile/workshop becomes active

Buildings can be **deconstructed**, returning some materials.

### 10.2 Workshop Catalogue

| Workshop | Inputs | Outputs | Required Skill |
|---|---|---|---|
| Carpenter's Workshop | Logs | Beds, Barrels, Bins, Buckets, Furniture, Wooden Items | Carpentry |
| Mason's Workshop | Stone blocks | Stone furniture, blocks, crafts | Masonry |
| Smelter | Ore + Fuel | Metal bars | Smelting |
| Metalsmith's Forge | Metal bars + Fuel | Weapons, armour, tools, misc | Smithing (sub-types) |
| Craftsdwarf's Workshop | Various | Crafts, mugs, figures, clothing | Various |
| Tanner's Shop | Raw hide | Leather | Tanning |
| Leatherworks | Leather | Armour, bags | Leatherworking |
| Loom | Thread/Yarn | Cloth | Weaving |
| Clothier's Shop | Cloth | Garments | Clothesmaking |
| Bowyer's Workshop | Wood/Bone | Bows, crossbows, bolts | Bowyer |
| Kitchen | Food items | Prepared meals | Cooking |
| Still | Plants/Fruit | Alcohol | Brewing |
| Butcher's Shop | Animal carcass | Meat, fat, bone, hide | Butchery |
| Fishery | Raw fish | Cleaned fish | Fishing/Processing |
| Farmers Workshop | Seeds, plants | Processed plants, bags | Farming |
| Mechanics Workshop | Stone/Metal | Mechanisms, traps | Mechanics |
| Siege Workshop | Wood/Metal | Catapults, ballista parts | Siege Engineering |
| Jeweler's Workshop | Gems + Items | Decorated items | Gem Cutting/Setting |
| Screw Press | Seeds/Fruit | Oil, slurry | Pressing |
| Ashery | Wood ash + Lye | Lye, potash | - |

### 10.3 Production Orders

Players add **Production Orders** to workshops:
- Specify recipe, quantity, repeat setting (once / repeat / until N stockpiled)
- Multiple orders queue up; dwarves process them in order
- "Manager" dwarf role can auto-generate orders from pre-set rules (e.g. "keep 20 barrels of alcohol stockpiled")

---

## 11. Stockpile System

Stockpiles are rectangular zones where items are stored. Each stockpile has a **filter configuration** specifying:
- Allowed item categories (food, stone, wood, metal, weapons, armour, furniture, etc.)
- Allowed materials (e.g. only iron/steel weapons)
- Max bins/barrels count

`StockpileManager` tracks:
- All stockpile zones and their contents
- Haul jobs: when an item is dropped on the ground undesignated, it scans for a matching stockpile and creates a `HaulJob`
- Query interface: `FindBestStockpile(ItemType, Vector3I nearPos)` used by workshops to locate inputs

**Bin/Barrel logic:** Small items are grouped into bins; liquids/food into barrels (hauling efficiency).

---

## 12. Items & Inventory

### 12.1 Item Data Model

```
class Item {
    int          Id
    string       ItemDefId    // references data/items.json
    MaterialId   Material
    Quality      Quality      // No quality / Inferior / … / Masterwork / Artifact
    float        Wear         // 0.0–1.0
    bool         IsEquipped
    EntityId     Owner        // 0 if unowned (on ground / in stockpile)
    Vector3I     Position     // world position (if dropped)
}
```

### 12.2 Materials & Quality

**Materials** are data-defined in `data/materials.json`. Each material has:
- Category (stone, metal, wood, leather, cloth, gem, bone, etc.)
- Properties: hardness, density, value multiplier, meltingPoint, flammable
- Allowed uses (weapon, armour, furniture, construction, etc.)

**Quality tiers** affect item value, dwarf happiness (when using/seeing fine items), and combat effectiveness:

No Quality → −− → − → (standard) → + → ++ → +++ → Artifact

Quality is determined at crafting time by a roll influenced by the craftsdwarf's skill level.

---

## 13. Resource & Production Chains

### 13.1 Mining & Stone

```
Dig Wall tile → Raw stone boulder drops
Boulder → Mason's Workshop → Stone block
Boulder → Craftsdwarf's Workshop → Stone crafts
Stone block → Construction (walls, floors, fortifications)
```

Ore tiles (magnetite, hematite, cassiterite, etc.) drop ore chunks on mining.

### 13.2 Woodcutting & Carpentry

```
Chop tree → Log
Log → Carpenter's Workshop → Planks / Furniture / Barrels / Bins / Beds / Doors / etc.
Log → Carpenter's → Ash (by burning) → Ashery → Potash (farming fertiliser)
```

### 13.3 Farming & Food Chain

```
Plump Helmet spores / other seeds → Farm Plot (underground) → Harvest → Raw plant
Rock nuts, pig tails, dimple cups etc. → various Farmers Workshop outputs
Raw plant → Kitchen → Prepared meal (increases food value)
Raw plant → Still → Alcohol (satisfies thirst + happiness bonus)
Fishing zone → Raw fish → Fishery → Cleaned fish → Kitchen
Hunting zone → Animal kill → Butcher → Meat / bone / fat / hide
```

Farm plots require:
- Underground (muddy soil from river or irrigation)
- Correct season (some plants surface-only)
- Farmer dwarf with Farming skill

### 13.4 Smelting & Metalworking

```
Ore chunk → Smelter (+ fuel: wood/charcoal/coke) → Metal bar
Metal bar → Metalsmith's Forge → Weapons / Armour / Anvils / Tools / Chains / etc.
Wood → Carpenter's Workshop → Charcoal (in Smelter, no extra fuel)
Coal seam → Mine → Raw coal → Smelter → Coke (better fuel)
```

Metal tier examples: copper < bronze < iron < steel < adamantine (endgame)

### 13.5 Brewing & Cooking

```
Plump helmets / cave wheat / etc. → Still → Plump Helmet Wine / Dwarven Ale / etc.
Food items → Kitchen → Lavish/Fine/Simple meals (different happiness bonuses)
```

Dwarves prefer alcohol over water. Lack of alcohol causes unhappiness; total absence causes moods.

### 13.6 Crafting & Luxury Goods

```
Rough gem (mined) → Jeweler's Workshop → Cut gem
Cut gem + any item → Jeweler's → Decorated item (gem-encrusted) → high trade value
Stone/Bone/Wood → Craftsdwarf's → Figurines, mugs, totems, crafts (trade goods)
Animal hide → Tanner's → Leather → Leatherworks → Armour/Bags
Thread (from loom/plants) → Loom → Cloth → Clothier → Garments
```

---

## 14. Room & Zone System

Rooms are defined by a closed area of walls/doors around a special building:

| Room Trigger | Room Type | Effect |
|---|---|---|
| Bed | Bedroom | Dwarf claims bedroom → happiness bonus |
| Table + Chair | Dining Room | Eating here gives happiness bonus based on room quality |
| Cabinet / Chest | Storage | Dwarf stores personal belongings |
| Statue / Engravings | Decoration | Room value increases → happiness |
| Weapon Rack | Barracks | Soldiers sleep and train here |
| Coffin | Tomb | For burial → mood benefits for friends/family |
| Bookcase | Library | Recreation/reading needs |

Room **quality** is calculated from the quality of all furniture and engravings contained within. Rooms are ranked (Meager → Modest → Adequate → Fine → Great → Grand → Royal) and affect dwarf happiness accordingly.

Zones (non-room areas):
- Fish zone (water tile area for fishing)
- Hunt zone (surface area for hunting)
- Gather zone (surface for plant gathering)
- Dump zone (items hauled here for melting/disposal)
- Meeting area (idle dwarves congregate here; improves social need)
- Hospital zone (injured dwarves brought here)
- Pasture zone (animals penned here)
- Training range (archery target area)

---

## 15. Military System

### 15.1 Squads & Scheduling

Players create **squads** of up to 10 dwarves assigned from the civilian population. Squad settings:
- Name, uniform, weapon preference
- Schedule (month-by-month): Train / Patrol / Inactive
- Alert status: Active (on duty) / Inactive (return to civilian jobs)

While Training or Patrolling, dwarves ignore civilian job queue.

### 15.2 Combat

Combat is **turn-based per-tick** (every game tick, each combatant takes one action):

Actions: Move toward enemy | Attack | Dodge | Block (with shield) | Retreat

**Attack resolution:**
1. Attacker rolls to-hit: base accuracy + weapon skill modifier + attacker agility
2. Defender rolls dodge: dodge skill + agility − attacker skill
3. If hit: calculate damage = weapon base damage × attacker strength modifier × material hardness
4. Damage is applied to a **specific body part** (chosen randomly, influenced by attack direction)
5. Body part health tracks injuries: bruise → cut → fracture → severed
6. Injuries outside armour coverage can lead to bleeding (health drain per tick)
7. Armour mitigates: coverage % × material toughness reduces damage

**Death:** when a critical body part (head, heart, spine) is destroyed or total blood lost exceeds threshold.

**Morale:** dwarves witnessing allies die or enemies being massive take a "saw death" thought penalty. Fleeing is not automatic — dwarves have `Willpower` stat that resists morale breaks.

### 15.3 Equipment & Uniform

Uniforms are defined per-squad as a set of item slots with material/type preferences:
- Head, Neck, Upper Body, Lower Body, Hands, Feet, Shield hand, Weapon hand

`EquipmentManager` scans stockpiles for matching items and creates haul + equip jobs.

### 15.4 Training & Skills

Military dwarves improve combat skills by:
- Sparring with other dwarves (in barracks) → Fighter, weapon skill
- Firing at archery targets → Bowman, Crossbowman
- Actual combat → fastest gains, but risky

Equipment requirements for sparring: wooden practice weapons (safe), real weapons (chance of injury).

---

## 16. Creature & Wildlife AI

Non-dwarf entities:

**Passive animals** (livestock, pets): wander in pasture zone, can be milked/sheared/butchered/trained. They have basic hunger need.

**Wild fauna**: surface creatures (deer, giant badgers, elk birds) wander randomly. Flee from dwarves. Can be hunted.

**Vermin**: rats, cave beetles — cosmetic but lower dwarf happiness when seen.

**Hostile creatures / goblins**: have a state machine:
```
States: Wander → Detect Threat → Approach → Combat → Flee (low health/morale)
```
Group AI for siege parties: squads path-find to gates/walls, attempt to destroy barriers.

**Megabeasts** (dragons, forgotten beasts): rare, powerful single entities. Seek the fortress, attack anything in their path. Require military response.

`CreatureManager` runs creature AI on a slower tick rate than dwarves (every 3–5 game ticks) to reduce CPU load.

---

## 17. World Events

### 17.1 Seasons & Weather

`WeatherSystem` uses the calendar to drive:
- Surface temperature range per season → affects crops, exposed dwarves
- Rain / snow events (visual and tile saturation effects)
- Frozen water tiles in winter (blocking water flow, accessible walking surface)
- Spring thaw: frozen tiles melt → potential flooding

### 17.2 Migrants

After the first year, every ~half-year the game schedules **migrant waves**:
- Wave size scales with fortress wealth and population
- Migrants arrive at the map edge, path-find to the fortress entrance
- Each migrant is a fully-generated dwarf with random traits, skills (profession-biased), and starting equipment
- Player receives announcement; migrants join the civilian labor pool

### 17.3 Caravans & Trade

Traders from three civilisations arrive annually (one per season):
- Dwarven Liaison (Spring): diplomatic visit, allows ordering next year's caravan contents
- Dwarven Caravan (Autumn): large trade goods haul
- Human Caravan (Summer)
- Elven Caravan (Spring/Summer, anti-deforestation preference)

**Trade mechanic:**
- Caravan parks at the Trade Depot (must be constructed and accessible)
- Player moves trade goods from stockpiles to the depot via haul jobs
- Trade screen: player brokers exchanges (goods for goods, value comparison)
- Broker skill affects trade value received

### 17.4 Sieges & Raids

After reaching certain wealth/population/year milestones, hostile factions begin sending raids:

**Scaling threat:**
- Year 1–2: goblin ambushes (small groups, hidden until near surface)
- Year 2–4: siege parties (larger groups, battering rams, siege towers)
- Year 4+: full sieges with multiple squads, siege engines

**Invasion mechanics:**
- Enemy forces spawn at map edge
- They path-find toward the fortress, attacking doors/walls if blocked
- Traps (stone-fall, cage, spike, weapon) intercept them
- Militarised dwarves are automatically alerted (if squad on Active alert)
- Drawbridges can be raised to seal off sections

### 17.5 Megabeasts & Wanderers

`MegabeastScheduler` spawns rare dangerous entities after multi-year delays:
- Forgotten Beasts: randomly generated creatures with random special attacks (web, breath, poison)
- Dragons, Hydras, Rocs (predefined creatures with unique stats)
- Necromancers: raise dead dwarves/animals as undead

---

## 18. Fluid Simulation (Water & Magma)

Cellular-automaton fluid simulation, processed every N ticks (not every tick — performance):

- Each tile holds `FluidLevel` 0–7
- Fluid flows to adjacent lower tiles, equalises levels
- Waterfalls: fluid falls down Z-levels instantly if floor removed
- Flooding: water/magma fills enclosed spaces
- Magma-water interaction: magma + water = obsidian floor + steam cloud (damages dwarves)
- Aquifer tiles: infinitely produce water when dug (must be channelled carefully)
- Pumps (mechanic building) can move fluids upward (powered by manual pump operator or gear assembly)

Fluid is **NOT simulated globally every tick** — only tiles adjacent to active fluid are in the "fluid active set". Dormant ponds don't process.

---

## 19. Temperature & Fire System

- Each tile has a temperature value
- Heat sources: magma, fire, smelters, forges
- Heat spreads to adjacent tiles slowly
- Flammable materials (wood, cloth) ignite when temperature exceeds their `IgnitionPoint`
- Fire tiles: spread to adjacent flammable tiles, emit smoke upward
- Smoke: fills tiles, causes dwarves to suffocate if prolonged exposure
- Fire suppression: dwarves with buckets can haul water to fire tiles (creates job automatically in emergency queue)

---

## 20. Rendering & Visual Layer

### 20.1 Tile Renderer

Each Z-level is a **Godot TileMapLayer** node. Tile atlases are organised:
- `tileset_stone.png` — all stone types with variation tiles
- `tileset_soil.png` — soil, mud, clay
- `tileset_construction.png` — player-built walls, floors
- `tileset_features.png` — ore veins, gems, special
- `tileset_fluid.png` — water levels (7 frames), magma
- `tileset_vegetation.png` — grass, trees, mushrooms, moss

When `TileChanged` event fires, `WorldRenderer` marks the chunk dirty and repaints only changed cells in the next render frame.

**Visual style options:**
- ASCII mode: classic DF tile characters rendered in a monospace font atlas
- Tile mode: graphical tile set (Kenney or custom)
- Option toggled in settings

### 20.2 Z-Level Camera

- `Camera2D` with zoom support
- Pan: middle-mouse drag or WASD
- Z-level: `[` / `]` keys (standard DF bindings)
- Floor levels above current Z shown as transparent overlay (reveals overhangs)
- A "ceiling" toggle dims tiles above to show the underground cliff face

### 20.3 Lighting

- Each tile stores a computed light value (0–255)
- Light sources: open sky (cascades down open tiles), torches, magma, smelters, fire
- Light propagates with falloff using a BFS flood-fill pass triggered on map change
- Tiles in player-designated burrows or rooms use the room's ambient light
- Rendered as a CanvasLayer modulate overlay (dark tiles colored very dark grey/black)

---

## 21. User Interface

### 21.1 HUD Overview

```
┌──────────────────────────────────────────────────────┐
│  [Speed Controls] [Date: 15 Granite, Year 3]  [FPS] │  ← Top bar
├──────────────────────────────────────────────────────┤
│                                                      │
│                  TILE VIEW                           │  ← Main viewport
│                                                      │
├──────────────────────────────────────────────────────┤
│ [Designate▼] [Build▼] [Zones▼] [Military] [Stockpile]│  ← Action toolbar
│ [Dwarves] [Items] [Workshops] [Trade] [Reports]      │
└──────────────────────────────────────────────────────┘
│ ANNOUNCEMENT LOG (scrollable)                        │  ← Bottom strip
└──────────────────────────────────────────────────────┘
```

Right-clicking a tile shows a **context menu**: tile info, any item here, any dwarf here, cancel designation.

### 21.2 Designation Toolbar

Dropdown menus with sub-options:
- **Designate:** Mine, Smooth, Engrave, Chop, Gather, Remove Designation
- **Channels:** Channel (remove floor, create ramp below), Up Ramp, Down Stairs, Up/Down Stairs
- All use drag-box selection mode; ESC cancels

### 21.3 Dwarf Viewer

List panel showing all dwarves with status icons (working, sleeping, eating, tantrum, injured). Clicking opens:
- **Dwarf Detail Screen:** Name, age, profession, mood, health, attributes, skills, thoughts, relationships, active job, enabled labors
- Labors are toggled with checkboxes
- Assign to/remove from military squad

### 21.4 Workshop & Production UI

Click a placed workshop to open its panel:
- Current order being processed (progress bar)
- Order queue (drag to reorder)
- Add new order: recipe dropdown + quantity + repeat mode
- Suspend/resume toggle

### 21.5 Stockpile Configuration

Click a stockpile zone to open:
- Name field
- Category tree (checkboxes): expand/collapse item categories and material sub-types
- Max bin/barrel limits
- "Take from links" / "Give to links": connect stockpile to workshop for automatic hauling pipelines

### 21.6 Military Screen

Two-tab panel:
- **Squads tab:** list of squads, create/disband, assign dwarves, set uniform, toggle Active/Inactive
- **Schedule tab:** monthly calendar grid (rows = squads, columns = months) → set Train/Patrol/Inactive per cell

### 21.7 Announcements & Log

Bottom strip scrolls recent announcements. Click one to **jump camera** to the relevant tile. Severity colour-coded:
- White: information (caravan arrived, migrant wave)
- Yellow: warning (food running low, dwarf unhappy)
- Red: danger (dwarf in combat, building destroyed, death)

**Full reports screen** opens a filterable log of all past events.

---

## 22. Data-Driven Design (Resources & JSON)

All game content lives in JSON files under `data/`. The `DataManager` autoload reads these at startup and builds in-memory dictionaries. Advantages: modding-friendly, easy to balance without recompiling.

**`materials.json` sample structure:**
```json
{
  "granite": {
    "category": "stone",
    "hardness": 7,
    "density": 2650,
    "value": 1,
    "flammable": false,
    "color": "#8a8080"
  },
  "iron": {
    "category": "metal",
    "hardness": 9,
    "density": 7874,
    "value": 10,
    "meltingPoint": 1538,
    "flammable": false,
    "color": "#aaaaaa"
  }
}
```

**`recipes.json` sample:**
```json
{
  "wooden_bed": {
    "workshop": "carpenters_workshop",
    "skill": "carpentry",
    "inputs": [{"item": "log", "quantity": 1}],
    "outputs": [{"item": "bed", "quantity": 1}],
    "baseTicks": 200
  }
}
```

---

## 23. Performance Strategy

Dwarf Fortress is notorious for performance decay. Key mitigations:

| Problem | Strategy |
|---|---|
| Too many dwarves/creatures updating every tick | Stagger AI updates — not all dwarves process every tick; spread load over 4–8 tick batches |
| Pathfinding bottleneck | Async A* via Task.Run; hierarchical pathfinding; path caching; connectivity pre-check |
| Fluid simulation | Only process "active fluid" tiles; dormant fluids skip simulation |
| Tile rendering | Only re-render dirty chunks; use TileMapLayer batch update, never per-tile Node2D |
| Item count growth | Item pooling; consolidate identical items in stockpiles into stacks |
| Save file size | GZip compression; tile data encoded as bitfields; incremental dirty-chunk saves |
| Job system scan | O(1) bucket-sort by priority; spatial grid for "nearest job" queries |
| Memory | Structs for tile data (value types on stack/contiguous heap); classes only for entities |

**Target performance:** 100 dwarves, 10000 items, 200 creatures, 96×96×100 map → stable 60 FPS at ×1 speed on a mid-range CPU.

---

## 24. Implementation Phases / Roadmap

### Phase 0 — Foundation (Weeks 1–4)
- [ ] Godot 4 + C# project setup, folder structure
- [ ] Autoload singletons: GameManager, TimeManager, EventBus, DataManager
- [ ] Tile data model (TileData struct)
- [ ] Chunk system and WorldMap
- [ ] Basic world generator (flat map with stone layers)
- [ ] Z-level rendering (TileMapLayer per Z-level)
- [ ] Camera pan + Z-level scroll

### Phase 1 — Dwarves & Jobs (Weeks 5–10)
- [ ] Dwarf entity with position and basic movement
- [ ] 3D A* pathfinder
- [ ] Job system (mine job only)
- [ ] Designation: dig tiles
- [ ] Tile updates on mine (wall → floor + item drop)
- [ ] Item drop rendering
- [ ] Hauling job + basic stockpile
- [ ] Simple HUD: dwarf list, job log

### Phase 2 — Needs & Survival (Weeks 11–16)
- [ ] Dwarf needs system (hunger, thirst, sleep)
- [ ] Food/drink items
- [ ] Eating / drinking jobs
- [ ] Sleep job + beds
- [ ] Mood + thoughts system
- [ ] Basic happiness display in dwarf panel
- [ ] Death (starvation, dehydration)

### Phase 3 — Buildings & Production (Weeks 17–26)
- [ ] Building placement UI
- [ ] ConstructJob pipeline
- [ ] Carpenter's Workshop + recipes
- [ ] Mason's Workshop
- [ ] Still + farming basics
- [ ] Stockpile filter UI
- [ ] Production order UI
- [ ] Full production chain loop (mine → smelt → forge)

### Phase 4 — World Events & Threats (Weeks 27–36)
- [ ] Migrant waves
- [ ] Caravan + trade depot + trade UI
- [ ] Basic goblin ambushes
- [ ] Military system: squads, uniforms, scheduling
- [ ] Combat system
- [ ] Traps + mechanisms

### Phase 5 — Depth & Polish (Weeks 37–50)
- [ ] Full fluid simulation (water + magma)
- [ ] Temperature + fire
- [ ] Full room system + room quality
- [ ] Relationships + full thoughts catalogue
- [ ] Mood breaks (tantrum, berserk, strange mood / artifacts)
- [ ] Medical system
- [ ] Full skill catalogue
- [ ] Engraving + artifact generation
- [ ] Siege escalation
- [ ] Megabeasts
- [ ] Save/load system
- [ ] Full UI polish
- [ ] ASCII ↔ graphical tile toggle
- [ ] Settings screen
- [ ] Performance profiling pass

### Phase 6 — Content & Balancing (Ongoing)
- [ ] All material definitions
- [ ] All creature definitions
- [ ] All recipe definitions
- [ ] Full announcement catalogue
- [ ] Legend entries (history log of events)
- [ ] Sound design
- [ ] Playtesting & balance

---

## 25. Open Design Questions

1. **Map size limits:** How large can the map be before hitting performance walls? Need to benchmark chunk streaming vs fully-loaded map.
2. **Combat depth vs complexity:** How granular should body-part simulation be? Full DF granularity (individual organs, tendons) vs simplified (head/torso/limbs)?
3. **ASCII vs graphical:** Should we commit to one visual style first or build a theme system from day one?
4. **Dwarf count cap:** DF suggests ~80 dwarves as soft cap. Should we hard-cap for performance or let players choose?
5. **Mod support:** Add a mod-loading system early (data-driven design helps, but mod file discovery needs planning)?
6. **Procedural names:** Need a syllable-chaining name generator for dwarves, places, artifacts, and historical events.
7. **Legends mode:** Scope for a read-only history viewer? Out of initial scope but worthwhile to track events from the start.
8. **Multiplayer:** Not in scope — DF fortress mode is single-player by design; state complexity makes it extremely difficult.

---

## 26. UI/UX Design Philosophy & Accessibility

> **Goal:** Keep 100% of Dwarf Fortress's simulation depth while removing the *interface* as a barrier to entry. The game should be hard because dwarves die in interesting ways — not because the player couldn't find the right menu.

### 26.1 Core Design Principles

| Principle | What It Means In Practice |
|---|---|
| **Show, don't make them search** | The UI surfaces critical information proactively; players should never have to dig through 5 menus to find out why a dwarf is unhappy |
| **Mouse-first, keyboard-deep** | Every action is reachable with the mouse; power users unlock keyboard shortcuts for everything |
| **Sensible defaults** | Stockpiles, labors, and production orders ship with defaults that keep a small fortress alive without configuration |
| **Progressive disclosure** | Simple view shown by default; advanced options revealed on demand via an "Advanced" toggle or expandable panel |
| **No silent failures** | If something isn't working (dwarf can't reach a job, stockpile is full, workshop is missing material), the game tells the player clearly and suggests a fix |
| **Pause is free** | The game never punishes players for pausing. Planning while paused is a first-class workflow |
| **Reversible actions** | Designations can be cancelled before a dwarf starts. Buildings can be deconstructed. There are few permanent irreversible mistakes in the early game |

---

### 26.2 Visual Language & Readability

**Graphical tiles as the default; ASCII as an opt-in style.**

The original DF uses ASCII because of its technical history, not by design intent. Our default look uses a clean, readable tile set:

- **Material colour-coding:** Stone walls use muted colour variations by material type (granite = warm grey, limestone = beige, obsidian = dark grey with a purple sheen). Players learn materials visually without reading text.
- **Ore veins** are always highlighted with a distinct colour pulse so players notice valuable deposits immediately.
- **Z-level depth cues:** Tiles further below the current Z-level fade toward a dark blue-grey tint. This gives instant depth perception when scrolling.
- **Designated tiles** get a semi-transparent orange/red tint overlay. Tiles with active jobs in progress get a working-tool icon. No more invisible designations.
- **Item icons on the ground:** Dropped items display a small icon (category symbol) rather than a raw letter. Hovering shows the full name.
- **Entity silhouettes:** Dwarves and creatures are identifiable at a glance — dwarves are short and stocky, goblins are darker, merchants wear cloaks, animals have species-appropriate shapes.
- **Status rings:** A thin coloured ring around a dwarf sprite communicates their state at a glance:
  - Green = working
  - Blue = resting/sleeping
  - Orange = idle (needs a job)
  - Red = hungry/thirsty/in pain
  - Purple = disturbed mood
  - Flashing red = combat / emergency
- **Fluid shading:** Water is a transparent blue overlay with animated ripple; magma is animated orange-red glow. Depth (0–7 levels) is shown by opacity intensity.
- **UI theme:** Dark-stone colour palette (like underground stone, not generic grey). Large readable fonts. High-contrast mode available.

---

### 26.3 Onboarding & Tutorial System

The single biggest barrier in DF is the complete absence of guidance. Our solution is a **layered tutorial** that teaches through doing, not walls of text.

#### First Fortress: Guided Campaign

The first time the player starts the game, they are offered:

> *"Play a guided first fortress (recommended)"* or *"Start free-play"*

The guided mode uses a pre-seeded, forgiving embark site (surface forest, rich minerals, no hostile animals, no aquifer). A non-intrusive narrator panel appears on the right side of the screen:

**Tutorial stage flow:**

1. **Arrival** — Camera pans to the starting 7 dwarves. Narrator explains who they are and what the goal is. A glowing arrow points to the first pickaxe.
2. **First designation** — "Click and drag to designate these tiles for mining" with the area pre-highlighted. The Designate → Mine button pulses.
3. **Watch and wait** — Dwarves mine automatically. Narrator explains job assignment while it happens. Player learns that they don't control dwarves directly.
4. **Stockpile setup** — Narrator hints that stone is cluttering the floor. Guides player to draw a stone stockpile zone. Explains why stockpiles matter.
5. **First room** — Guide player to build a Carpenter's Workshop and make beds, then designate a barracks zone. Explains needs.
6. **Food & drink** — Narrator warns that food will run out. Walks through planting a farm plot and building a Still.
7. **First winter** — Season changes; narrator explains seasonal rhythms and warns to prepare.
8. **First caravan** — Trader arrives; guided trade UI walkthrough.
9. **First threat** — A small goblin scouting party triggers. Guide player through creating a squad and equipping them.
10. **You're on your own** — Tutorial ends with a summary card of what was learned and a link to the in-game wiki.

#### Contextual Hint System (always active, non-intrusive)

A **Hint Bubble** (small `ℹ` icon, top-right of any panel) appears whenever a new UI element is opened for the first time. Clicking it shows a brief explanation. These can be dismissed individually and globally toggled off in settings. They are never blocking — they never appear as modal popups.

#### In-Game Encyclopedia

`F1` opens a searchable reference wiki embedded in the game:
- Every material, creature, item, and building has its own article with recipes, uses, and tips
- Articles are generated from the same JSON data that drives the game → always accurate
- Players can also middle-click any tile, item, or entity to open its encyclopedia article directly

---

### 26.4 Smart Notifications & Proactive Guidance

#### The Advisor System

Replacing DF's silent simulation with a proactive **Advisor Panel** — a slim sidebar that shows up to 5 active warnings sorted by urgency. It is never a modal; players can ignore it entirely:

```
┌─────────────────────────────────────────────────┐
│ ⚠ ADVISOR                              [dismiss]│
├─────────────────────────────────────────────────┤
│ 🍺 Alcohol supply low (3 days left)    [→ Fix]  │
│ 😴 8 dwarves have no bedroom           [→ Fix]  │
│ 🛑 Workshop idle — missing iron bars   [→ Fix]  │
│ 😠 Urist McAxedwarf is very unhappy    [→ View] │
│ 📦 Finished goods stockpile full       [→ View] │
└─────────────────────────────────────────────────┘
```

Each `[→ Fix]` button does something smart:
- **Alcohol low** → opens the Still's production queue with a pre-filled "brew 20 units" order for one click
- **No bedroom** → enters zone-placement mode with Bedroom pre-selected
- **Workshop idle** → jumps camera to workshop, opens its panel, highlights which material is missing and where to find it
- **Unhappy dwarf** → opens the dwarf's Thoughts panel with the top problem highlighted
- **Stockpile full** → opens stockpile config and suggests expanding or creating a dump zone

#### Announcement Log Improvements vs DF

| DF Original | Our Version |
|---|---|
| Text floods in, oldest entries lost | Grouped by category (Combat / Economy / Social / System); each collapsible |
| Clicking jumps camera but does nothing else | Clicking opens a **context card** with relevant info + action buttons |
| No severity filtering | Filter bar: show All / Warnings / Danger / Info |
| No history | Persistent log scrollable back to fortress founding |
| Dense text | Each entry has a category icon and colour; single-line summary with expand arrow for detail |

#### Proactive Danger Warnings

The following conditions trigger **timed pause suggestions** (game speed drops to ×1 and a banner appears — player can dismiss or pause):
- Dwarf begins starving
- Combat begins within fortress bounds
- A dwarf enters a mood break (tantrum/berserk)
- A forgotten beast / megabeast enters the map
- Flood detected (rapid fluid level rise)
- Fire spreading to structures

These are suggestions, not forced pauses. A setting can escalate them to auto-pause.

---

### 26.5 Context-Sensitive Interaction

#### Right-Click Context Menu (universal)

Right-clicking **anything** in the world opens a context menu appropriate to what was clicked:

**On a wall tile:**
```
┌─────────────────────────────┐
│ Granite Wall                │
│ ─────────────────────────── │
│ ⛏ Designate to Mine         │
│ 🔨 Smooth Stone             │
│ ✏  Engrave                  │
│ ℹ  View Material Info       │
└─────────────────────────────┘
```

**On a dwarf:**
```
┌─────────────────────────────┐
│ Urist McBrewer  😊 Content  │
│ ─────────────────────────── │
│ 👤 View Dwarf Details       │
│ 🔨 Enable/Disable Labors    │
│ ⚔  Assign to Squad         │
│ 📍 Follow Camera            │
└─────────────────────────────┘
```

**On a workshop:**
```
┌─────────────────────────────┐
│ Carpenter's Workshop        │
│ Status: Working (Beds)      │
│ ─────────────────────────── │
│ 📋 Open Production Queue    │
│ ▶/⏸ Resume / Suspend        │
│ 🔗 Link to Stockpile        │
│ 💣 Deconstruct              │
└─────────────────────────────┘
```

#### Hover Tooltips

Hovering over any tile, entity, or item for 0.5 seconds shows a rich tooltip:
- Tile: material, designation status, fluid level, room membership, light level
- Dwarf: name, mood emoji, current job, critical need if any
- Item: full name (e.g. "Masterwork iron war hammer"), material, quality, owner
- Building: type, current job, skill requirement, assigned workers

Tooltip content is generated dynamically from game data — always accurate, no hardcoded strings.

---

### 26.6 Reworked Labor Management

DF's labor system is a 70-checkbox grid per dwarf — one of the most criticised UI elements. We replace it with a **role-based system with optional fine control.**

#### Dwarf Roles (default view)

Each dwarf is assigned a **Role** — a named preset of enabled labors:

| Role | Enabled Labors |
|---|---|
| Miner | Mining, Hauling |
| Builder | Construction, Masonry, Carpentry, Hauling |
| Crafter | All workshop skills, Hauling |
| Farmer | Farming, Cooking, Brewing, Hauling |
| Soldier | Military only (no civilian labors while active) |
| Doctor | Medical, Hauling |
| Hauler | Hauling only |
| Peasant | Hauling + miscellaneous cleaning/maintenance |
| Idle (all) | Every labor enabled — for unspecialized settlers |

Roles are assigned with a single dropdown in the Dwarf Panel — no more checkbox hunting. Players can still click **"Advanced Labors"** to get the full per-labor toggle grid for fine control.

#### Workforce Overview Panel

A dedicated **Workforce** screen (new) gives a spreadsheet view of all dwarves with their current role, mood, and job. Players can:
- Multi-select dwarves and bulk-assign a role
- Sort by mood, skill level, profession, name, current status
- Filter for "idle dwarves" or "unhappy dwarves" to take action quickly
- See at a glance if a labor category is understaffed (e.g. only 1 dwarf with Mining enabled)

---

### 26.7 Reworked Stockpile Management

#### Smart Defaults

When a player draws a new stockpile zone, instead of a blank slate they see a smart suggestion:
- The game analyses the current map and suggests the most needed stockpile category
- One-click presets: **"Food & Drink"**, **"Stone & Ore"**, **"Wood"**, **"Finished Goods"**, **"Weapons & Armour"**, **"Everything"**
- Presets can be customised after placement

#### Visual Stockpile Overlay

A **Stockpile Mode** view (toggle button or `S` key) overlays coloured fills on the map:
- Each stockpile zone shows its fill percentage as a colour intensity (empty = pale, full = saturated)
- Hovering shows: current item count, max capacity, top 3 stored items
- Items sitting on the ground outside any stockpile are highlighted in blinking orange — a passive reminder to haul them

#### Auto-Linking

When a workshop is placed near a stockpile whose contents match the workshop's inputs, the game offers:
> *"Link this workshop to the nearby Stone stockpile for automatic input? [Yes] [No] [Ask later]"*

This replaces the buried "Give to / Take from links" config that most new players never discover.

---

### 26.8 Production Automation & Manager AI

Manually managing every production queue in DF is exhausting. We introduce an **Automation Layer** controlled from a simple panel:

#### Supply Rules

Players define **Supply Rules** instead of (or alongside) manual production orders:

```
┌──────────────────────────────────────────────────────────────┐
│ SUPPLY RULES                              [+ Add Rule]       │
├──────────────────────────────────────────────────────────────┤
│ ✅ Keep at least 30 units of Food in stockpile               │
│    → Kitchen: cook meals from available ingredients          │
├──────────────────────────────────────────────────────────────┤
│ ✅ Keep at least 40 barrels of Alcohol                       │
│    → Still: brew from plump helmets                          │
├──────────────────────────────────────────────────────────────┤
│ ✅ Keep at least 10 beds in stockpile                        │
│    → Carpenter: craft beds from logs                         │
├──────────────────────────────────────────────────────────────┤
│ ☐  Keep at least 5 iron swords                               │
│    → Forge: smith iron swords                                │
└──────────────────────────────────────────────────────────────┘
```

The Manager AI checks stockpile levels each in-game day and auto-queues production orders when a threshold is not met. This replaces the DF "manager" role + work orders system with something immediately understandable.

Players can still add one-off manual orders on any workshop if they want finer control.

#### Starter Supply Pack

On fortress start, the game automatically activates a **Starter Supply Pack** — a default set of 5–6 safe supply rules (food, drink, beds, basic crafts) that keep small fortresses alive. Players can deactivate any rule at any time. This effectively means a new player's fortress doesn't immediately spiral into starvation just because they forgot to queue up brewing.

---

### 26.9 Map Navigation & Overview

#### Minimap

A resizable minimap panel in the corner displays:
- The entire embark in top-down view (all Z-levels collapsed, coloured by topmost material)
- Current viewport as a rectangle overlay
- Stockpile zones, buildings, and dwarves as colour-coded dots
- Click to pan camera to that location
- Scroll wheel on minimap changes Z-level

#### Z-Level Navigation

| DF Original | Our Version |
|---|---|
| `<` and `>` keys only | `[` / `]` keys + scroll wheel over the map |
| No visual indication of z depth | Z-level counter prominently shown; a vertical "depth slider" bar on the left edge |
| Opaque, hard to understand overhangs | Tiles below current floor shown as shadows; tiles above shown translucent |
| No quick-jump | Double-clicking the depth slider bar jumps to Surface / Lowest room / Magma sea |

#### Named Bookmarks

Players can press `B` to drop a bookmark at any location with a custom label ("Main Forge", "Dining Hall", "Goblin Entry"). A bookmark toolbar at the top right (up to 8 slots) lets them teleport the camera instantly. Crucial for large multi-Z fortresses.

#### Fortress Overview Mode

Press `Tab` to switch from detailed tile view to a **Fortress Overview** — a zoomed-out semantic map:
- Rooms are shown as labelled boxes ("Bedroom ×12", "Dining Room", "Smelter")
- Paths between rooms shown as corridors
- Dwarves shown as dots coloured by mood
- Enemies shown as hostile red markers
- Useful for getting the "big picture" without scrolling across every Z-level

---

### 26.10 Input Design: Mouse-First with Keyboard Depth

| Action | Mouse | Keyboard shortcut |
|---|---|---|
| Mine designation | Click Mine button → drag | `M` then drag |
| Build workshop | Click Build menu → click tile | `B` then sub-key |
| Pause / Resume | Click pause button | `Space` |
| Speed up / down | Click speed buttons | `+` / `-` |
| Z-level up / down | Scroll wheel | `[` / `]` |
| Cancel current tool | Click X in toolbar | `Esc` |
| Open Dwarf list | Click Dwarves icon | `D` |
| Open Military | Click Military icon | `A` (army) |
| Open Stockpiles | Click Stockpile icon | `K` (keep) |
| Jump to announcement | Click in log | `Enter` on selected log entry |
| Pan camera | Middle-drag or edge-scroll | `W` `A` `S` `D` |
| Open encyclopedia | Click `ℹ` on tooltip | `F1` |
| Drop bookmark | — | `B` |
| Fortress overview | — | `Tab` |

All shortcuts are **remappable** in settings. An in-game **shortcut cheatsheet** (press `?`) shows every available key.

Right-hand mouse button is used **exclusively** for context menus — never for camera movement (eliminates accidental menu triggers).

---

### 26.11 Pause-and-Plan Workflow

DF veterans use pause extensively. We make this a first-class supported pattern:

- **While paused, all UI panels remain fully interactive** — players can place designations, queue production orders, configure stockpiles, and assign labors. The world is frozen but all planning tools work.
- A **Plan Mode indicator** (dim vignette + "PAUSED" banner) makes it visually clear the game is paused.
- **Undo Designation** — while paused, a player can undo their most recent designation box (up to 10 undo steps, scoped to designations not yet started by a dwarf).
- Announcements that trigger auto-pause give a brief **context card** explaining the situation and offering 2–3 common response actions as buttons, all executable while still paused.

---

### 26.12 Difficulty & Pacing Settings

Rather than a single difficulty slider, present players with **Pacing Knobs** at embark setup:

| Setting | Options | Effect |
|---|---|---|
| **Threat Grace Period** | None / 1 year / 2 years / 3 years | Delays first goblin ambush; gives players time to establish |
| **Raid Escalation** | Slow / Normal / Fast / Brutal | Controls how quickly sieges grow in size |
| **Megabeast Frequency** | Rare / Normal / Frequent / Off | Controls megabeast spawn timing |
| **Need Decay Rate** | Slow / Normal / Fast | How quickly dwarves get hungry/thirsty — Slow is more forgiving |
| **Starting Resources** | Minimal / Standard / Generous | Amount of food/drink/materials in the starting wagon |
| **Caravan Frequency** | Annual (default) / More frequent | More frequent = easier access to trade goods |
| **Advisor System** | Full / Hints only / Off | Controls how much the Advisor panel intervenes |
| **Auto Supply Rules** | On (default) / Off | Whether starter supply rules are active from day 1 |

Preset bundles:
- **Story mode** — Long grace period, slow decay, generous start, full advisor. Designed for players who want the story, not the challenge.
- **Classic** — Normal everything, advisor hints only. Closest to original DF feel with better UI.
- **Irondwarf** — No grace period, fast escalation, minimal resources, no advisor. For veterans wanting maximum challenge.

---

### 26.14 Quality-of-Life Features

| Feature | Description |
|---|---|
| **Auto-save** | Saves every 3 in-game seasons (configurable). Save slot shown in top bar with timestamp. |
| **Multiple save slots** | 5 manual save slots + 3 auto-save rotation slots. Prevents overwriting a good state. |
| **Undo Designation** | Cancel the last N drag-designations while paused (before dwarves claim them). |
| **Area Copy/Paste** | Select a rectangular area, copy its designation pattern, paste it elsewhere. Useful for symmetrical rooms. |
| **Blueprint Mode** | Place a building layout as a ghost overlay without immediately triggering construction, allowing review before committing. |
| **Zoom to Dwarf** | Double-click any dwarf in the Dwarf list panel to centre the camera on them. |
| **Follow Dwarf** | Click Follow in a dwarf's context menu → camera tracks them in real time. |
| **Item count overlays** | Toggle `I` to show floating number labels on stockpiles (shows total item count per zone). |
| **Production chain lookup** | Hovering a recipe input in a workshop shows a chain tooltip: "Iron bar comes from: Iron ore (Smelter) → Mine magnetite / hematite". |
| **Dwarf alert colours in list** | The dwarf list automatically sorts distressed dwarves to the top when they need attention. |
| **Confirm before deconstruct** | Any deconstruction action that would drop items into a flooded or inaccessible area shows a warning. |
| **Remembered camera positions** | The game restores the exact camera position and Z-level when loading a save. |
| **Batch designation shapes** | In addition to drag-box, support flood-fill designation (fill connected area) and ring designation (hollow rectangle for room outlines). |

---

*Plan version 1.1 — March 2026 (added Section 26: UI/UX Design Philosophy & Accessibility)*
