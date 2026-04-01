# Extensible Game Architecture
### Dwarf Fortress Clone — Godot 4 / C#

> **Purpose of this document:** Define the software architecture patterns, contracts, and wiring strategies that allow every piece of game content (tiles, items, creatures, jobs, workshops, events…) to connect to every other piece with minimal coupling and maximum extensibility. New content should be addable by editing JSON files and/or dropping in a new C# class — never by modifying existing systems.

---

## Table of Contents

1. [Guiding Principles](#1-guiding-principles)
2. [Multi-Project Solution Structure](#2-multi-project-solution-structure)
   - 2.1 [Project Dependencies](#21-project-dependencies)
   - 2.2 [Platform-Agnostic Types in GameLogic](#22-platform-agnostic-types-in-gamelogic)
   - 2.3 [Injected Platform Interfaces](#23-injected-platform-interfaces)
   - 2.4 [GameSimulation: The Public Entry Point](#24-gamesimulation-the-public-entry-point)
   - 2.5 [Bootstrap: Godot Side](#25-bootstrap-godot-side)
   - 2.6 [The GodotEventBridge](#26-the-godoiteventbridge)
   - 2.7 [The Command Dispatcher](#27-the-command-dispatcher)
   - 2.8 [GameState Snapshots](#28-gamestate-snapshots)
3. [The Three-Layer Model](#3-the-three-layer-model)
4. [Content Definition Layer (Data)](#4-content-definition-layer-data)
   - 4.1 [Definition vs Instance](#41-definition-vs-instance)
   - 4.2 [The Tag System](#42-the-tag-system)
   - 4.3 [Effect & Modifier Blocks](#43-effect--modifier-blocks)
   - 4.4 [Content Registries](#44-content-registries)
   - 4.5 [Cross-References via String IDs](#45-cross-references-via-string-ids)
5. [Simulation Layer (Systems)](#5-simulation-layer-systems)
   - 5.1 [System Interface Contract](#51-system-interface-contract)
   - 5.2 [The EventBus (Decoupled Wiring)](#52-the-eventbus-decoupled-wiring)
   - 5.3 [The Reaction Pipeline](#53-the-reaction-pipeline)
   - 5.4 [The Modifier Stack](#54-the-modifier-stack)
   - 5.5 [The Needs/Effects Applicator](#55-the-needseffects-applicator)
6. [Entity Architecture](#6-entity-architecture)
   - 6.1 [Entity Identity & Lifecycle](#61-entity-identity--lifecycle)
   - 6.2 [Component Bag Pattern](#62-component-bag-pattern)
   - 6.3 [Behaviour Trees for AI](#63-behaviour-trees-for-ai)
   - 6.4 [Stat & Attribute Derivation Graph](#64-stat--attribute-derivation-graph)
7. [Job & Action Architecture](#7-job--action-architecture)
   - 7.1 [Job as Data + Strategy](#71-job-as-data--strategy)
   - 7.2 [Action Steps (Sub-Tasks)](#72-action-steps-sub-tasks)
   - 7.3 [Preconditions & Postconditions](#73-preconditions--postconditions)
   - 7.4 [Adding a New Job Type (walkthrough)](#74-adding-a-new-job-type-walkthrough)
8. [Material & Item Architecture](#8-material--item-architecture)
   - 8.1 [Material Property Inheritance](#81-material-property-inheritance)
   - 8.2 [Item Template + Runtime Instance](#82-item-template--runtime-instance)
   - 8.3 [Adding a New Material (walkthrough)](#83-adding-a-new-material-walkthrough)
9. [Recipe & Production Architecture](#9-recipe--production-architecture)
   - 9.1 [Generic Recipe Schema](#91-generic-recipe-schema)
   - 9.2 [Input Matching with Tags](#92-input-matching-with-tags)
   - 9.3 [Output Generation Rules](#93-output-generation-rules)
   - 9.4 [Adding a New Workshop + Recipes (walkthrough)](#94-adding-a-new-workshop--recipes-walkthrough)
10. [Tile & World Architecture](#10-tile--world-architecture)
    - 10.1 [Tile Behaviour via Tile Traits](#101-tile-behaviour-via-tile-traits)
    - 10.2 [Tile Change Propagation](#102-tile-change-propagation)
    - 10.3 [Adding a New Tile Type (walkthrough)](#103-adding-a-new-tile-type-walkthrough)
11. [Creature Architecture](#11-creature-architecture)
    - 11.1 [Creature Definition Schema](#111-creature-definition-schema)
    - 11.2 [Creature AI via Behaviour Modules](#112-creature-ai-via-behaviour-modules)
    - 11.3 [Adding a New Creature (walkthrough)](#113-adding-a-new-creature-walkthrough)
12. [World Event Architecture](#12-world-event-architecture)
    - 12.1 [Event Definition Schema](#121-event-definition-schema)
    - 12.2 [Trigger Conditions](#122-trigger-conditions)
    - 12.3 [Event Chains & Consequences](#123-event-chains--consequences)
    - 12.4 [Adding a New World Event (walkthrough)](#124-adding-a-new-world-event-walkthrough)
13. [Inter-System Communication Map](#13-inter-system-communication-map)
14. [Dependency & Initialization Order](#14-dependency--initialization-order)
15. [Mod & Extension Loading](#15-mod--extension-loading)
16. [Testing Strategy](#16-testing-strategy)
17. [Architecture Anti-Patterns to Avoid](#17-architecture-anti-patterns-to-avoid)

---

## 1. Guiding Principles

Every architectural decision in this codebase is governed by these five rules:

### 1. Data Over Code
New content (materials, creatures, recipes, events, jobs) is defined in **JSON data files**, not in new C# classes. A new ore should require zero new code — only a new entry in `materials.json`. Code only changes when behaviour is fundamentally new.

### 2. Open/Closed Principle
Systems are **open for extension, closed for modification**. Adding a new workshop type should never require editing `Workshop.cs`. It should require only a new JSON entry and optionally a new `IWorkshopBehaviour` strategy class.

### 3. Communicate via Events, Not References
Systems never hold direct references to other systems. They emit events onto the `EventBus` and subscribe to events they care about. A new system can hook into any part of the simulation just by subscribing to the right events — no existing code touched.

### 4. Tags as the Universal Connector
Items, tiles, creatures, needs, recipes, and jobs all carry **string tags**. Tags are the universal language that connects content. A recipe doesn't say "requires 1 iron bar" — it says "requires 1 item tagged `[metal][refined]`". This means adding a new smelted metal automatically makes it usable in every recipe that accepts refined metals.

### 5. Definitions are Immutable; Instances are Mutable
A `MaterialDef` read from JSON never changes at runtime. A `MaterialInstance` on a tile or item carries runtime state (temperature, contamination). Keeping definitions read-only means they can be safely shared and cached.

### 6. Engine Agnosticism
The simulation layer (`DwarfFortress.GameLogic`) contains zero references to Godot. It compiles and runs as a plain .NET 8 class library. This enables unit and integration tests to run with `dotnet test` — no Godot installation required — and enables a headless console runner for profiling and debugging. Rendering is treated as a plugin that wraps the simulation, not the other way around. See Section 2 for the complete multi-project breakdown.

---

## 2. Multi-Project Solution Structure

The game is built as a **.NET solution with three separate C# projects**. The simulation knows nothing about Godot; Godot knows only how to render simulation state and forward player input.

```
DwarfFortress.sln
├── DwarfFortress.GameLogic/     ← Pure .NET 8 class library  (no Godot SDK)
│   ├── Core/
│   │   ├── IGameSystem.cs
│   │   ├── ILogger.cs
│   │   ├── IDataSource.cs
│   │   ├── EventBus.cs          // pure C# delegate-based event bus
│   │   ├── CommandDispatcher.cs
│   │   ├── GameContext.cs
│   │   ├── GameSimulation.cs    // top-level entry point
│   │   └── Vec3i.cs             // engine-agnostic integer vector
│   ├── Data/                    // DataManager, Registry<T>, all *Def records
│   ├── Systems/                 // all IGameSystem implementations
│   ├── Entities/                // Entity, Dwarf, Creature, Item, Building
│   ├── World/                   // WorldMap, Chunk, WorldGenerator, Pathfinder
│   ├── Jobs/                    // JobSystem, Job, IJobStrategy, strategies
│   └── Snapshots/               // read-only view-model records for the UI
│
├── DwarfFortress.Godot/         ← Godot 4 project  (.NET 8 + Godot SDK)
│   ├── Bridge/
│   │   ├── GodotEventBridge.cs  // GameLogic C# events → Godot signals
│   │   ├── GodotDataSource.cs   // IDataSource using Godot FileAccess
│   │   ├── GodotLogger.cs       // ILogger using GD.Print / GD.PrintErr
│   │   └── GodotCommandDispatcher.cs
│   ├── Bootstrap/
│   │   └── GameBootstrapper.cs  // constructs GameSimulation, wires bridge
│   ├── Rendering/
│   │   ├── WorldRenderer.cs     // reads ChunkViewSnapshot → TileMapLayer
│   │   └── EntityRenderer.cs
│   └── UI/
│       ├── HUDController.cs
│       ├── DwarfPanel.cs
│       └── ...
│
└── DwarfFortress.Tests/         ← xUnit project  (.NET 8, no Godot)
    ├── Fakes/
    │   ├── InMemoryDataSource.cs // test double for IDataSource
    │   ├── TestLogger.cs         // captures log output for assertions
    │   └── TestEventBus.cs       // synchronous, inspectable event bus
    ├── SimulationTests/
    │   ├── JobSystemTests.cs
    │   ├── FluidSimulatorTests.cs
    │   ├── RecipeResolverTests.cs
    │   └── NeedsSystemTests.cs
    ├── WorldTests/
    │   ├── PathfinderTests.cs
    │   └── TileTraitTests.cs
    └── ConsoleRunner/
        └── ConsoleGameRunner.cs  // headless simulation for profiling / debugging
```

### 2.1 Project Dependencies

```
DwarfFortress.Tests ──────────────────────────────────────────▶ DwarfFortress.GameLogic
DwarfFortress.Godot ──▶ Godot.NET.Sdk (engine bindings)
DwarfFortress.Godot ──────────────────────────────────────────▶ DwarfFortress.GameLogic
```

`DwarfFortress.GameLogic` **references no Godot assemblies whatsoever**. It targets `net8.0` (not an engine-specific TFM). It can be compiled and tested with a plain `dotnet test` — no Godot installation needed.

### 2.2 Platform-Agnostic Types in GameLogic

Godot-specific types must never appear in `GameLogic`. Every occurrence is replaced with a standard alternative:

| Prohibited Godot Type | GameLogic Replacement | Reason |
|---|---|---|
| `Vector3I` | `Vec3i` (custom record struct) | Godot assembly dependency |
| `Vector2I` | `Vec2i` (custom record struct) | Same |
| `GD.Print / GD.PrintErr` | `ILogger` (injected interface) | Godot static class |
| `FileAccess` | `IDataSource` (injected interface) | Godot file I/O |
| `Node` base class | None — plain C# POCO classes | Godot scene system |
| `[Signal]` / `[Export]` | None — C# events/properties | Godot reflection attributes |
| `Godot.Collections.*` | `System.Collections.Generic.*` | Engine-specific collections |

```csharp
// Core/Vec3i.cs — lives in GameLogic, zero Godot dependency
public readonly record struct Vec3i(int X, int Y, int Z) {
    public static readonly Vec3i Zero = new(0,  0,  0);
    public static readonly Vec3i Up   = new(0,  0,  1);
    public static readonly Vec3i Down = new(0,  0, -1);

    public Vec3i Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public IEnumerable<Vec3i> Neighbours6() {
        yield return new(X+1, Y,   Z);   yield return new(X-1, Y,   Z);
        yield return new(X,   Y+1, Z);   yield return new(X,   Y-1, Z);
        yield return new(X,   Y,   Z+1); yield return new(X,   Y,   Z-1);
    }
}
```

### 2.3 Injected Platform Interfaces

GameLogic declares interfaces for every service that differs between production (Godot) and test environments:

```csharp
// Core/IDataSource.cs
public interface IDataSource {
    string   ReadText  (string path);
    string[] ListFiles (string directory, string extension = ".json");
    bool     Exists    (string path);
}

// Core/ILogger.cs
public interface ILogger {
    void Info  (string msg);
    void Warn  (string msg);
    void Error (string msg);
    void Debug (string msg);
}
```

**Godot implementations** (in `DwarfFortress.Godot/Bridge/`):
```csharp
public class GodotDataSource : IDataSource {
    public string ReadText(string path) {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f.GetAsText();
    }
    public string[] ListFiles(string dir, string ext = ".json") =>
        DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(ext)).ToArray();
    public bool Exists(string path) => FileAccess.FileExists(path);
}

public class GodotLogger : ILogger {
    public void Info (string msg) => GD.Print($"[INFO]  {msg}");
    public void Warn (string msg) => GD.Print($"[WARN]  {msg}");
    public void Error(string msg) => GD.PrintErr($"[ERROR] {msg}");
    public void Debug(string msg) => GD.Print($"[DEBUG] {msg}");
}
```

**Test doubles** (in `DwarfFortress.Tests/Fakes/`):
```csharp
public class InMemoryDataSource : IDataSource {
    private readonly Dictionary<string, string> _files = new();
    public void   AddFile  (string path, string content) => _files[path] = content;
    public string ReadText (string path) => _files[path];
    public string[] ListFiles(string dir, string ext = ".json") =>
        _files.Keys.Where(k => k.StartsWith(dir) && k.EndsWith(ext)).ToArray();
    public bool   Exists   (string path) => _files.ContainsKey(path);
}

public class TestLogger : ILogger {
    public List<string> Errors { get; } = new();
    public void Info (string msg) { }
    public void Warn (string msg) { }
    public void Error(string msg) => Errors.Add(msg);
    public void Debug(string msg) { }
}
```

### 2.4 GameSimulation: The Public Entry Point

`GameSimulation` is the top-level class in `GameLogic`. It is a plain C# class with no Godot inheritance:

```csharp
// Core/GameSimulation.cs
public sealed class GameSimulation {
    public EventBus          EventBus          { get; }
    public CommandDispatcher CommandDispatcher { get; }
    public GameContext       Context           { get; }
    private readonly List<IGameSystem> _systems = new();
    private readonly ILogger           _logger;

    public GameSimulation(ILogger logger, IDataSource dataSource) {
        _logger           = logger;
        EventBus          = new EventBus();
        CommandDispatcher = new CommandDispatcher();
        Context           = new GameContext(EventBus, CommandDispatcher, logger, dataSource);
    }

    public void RegisterSystem(IGameSystem system) => _systems.Add(system);

    // Called by the host (GameBootstrapper or ConsoleRunner)
    public void RegisterDefaultSystems() {
        RegisterSystem(new DataManager(Context));
        RegisterSystem(new EntityRegistry());
        RegisterSystem(new WorldMap(Context));
        // ... all other systems in UpdateOrder
    }

    public void Initialize() {
        foreach (var s in _systems.OrderBy(s => s.UpdateOrder))
            s.Initialize(Context);
        _logger.Info("GameSimulation initialized.");
    }

    public void Tick(float delta) {
        foreach (var s in _systems.Where(s => s.IsEnabled))
            s.Tick(delta);
    }

    public void Save(string slot) { /* delegates to SaveSystem */ }
    public void Load(string slot) { /* delegates to SaveSystem */ }
}
```

### 2.5 Bootstrap: Godot Side

`GameBootstrapper` in `DwarfFortress.Godot` constructs the simulation and wires it to the Godot scene graph:

```csharp
// Bootstrap/GameBootstrapper.cs  (Godot project)
public partial class GameBootstrapper : Node {
    private GameSimulation _sim;

    public override void _Ready() {
        // 1. Build platform adapters (Godot implementations of GameLogic interfaces)
        var logger     = new GodotLogger();
        var dataSource = new GodotDataSource();

        // 2. Construct and boot the pure-C# simulation
        _sim = new GameSimulation(logger, dataSource);
        _sim.RegisterDefaultSystems();
        _sim.Initialize();

        // 3. Connect simulation events → Godot rendering + UI signals
        GetNode<GodotEventBridge>("EventBridge").Attach(_sim.EventBus);

        // 4. Connect Godot player input → simulation Commands
        GetNode<InputHandler>("InputHandler").Attach(_sim.CommandDispatcher);
    }

    public override void _Process(double delta) => _sim.Tick((float)delta);
}
```

### 2.6 The GodotEventBridge

The only place where GameLogic C# events are forwarded as Godot signals (needed because Godot UI Control nodes communicate via Godot's signal system):

```csharp
// Bridge/GodotEventBridge.cs  (Godot project)
public partial class GodotEventBridge : Node {
    [Signal] public delegate void TileChangedEventHandler    (int x, int y, int z);
    [Signal] public delegate void DwarfMoodChangedEventHandler(int dwarfId, int mood);
    [Signal] public delegate void AnnouncementEventHandler   (string text, int severity);
    [Signal] public delegate void DwarfSnapshotEventHandler  (int id, string json);

    public void Attach(EventBus bus) {
        // CallDeferred ensures signals fire on the Godot main thread
        // even if Tick() is eventually moved to a worker thread
        bus.On<TileChangedEvent> (e => CallDeferred(
            MethodName.EmitSignal, SignalName.TileChanged, e.Pos.X, e.Pos.Y, e.Pos.Z));
        bus.On<MoodChangedEvent> (e => CallDeferred(
            MethodName.EmitSignal, SignalName.DwarfMoodChanged, e.DwarfId, (int)e.Mood));
        bus.On<AnnouncementEvent>(e => CallDeferred(
            MethodName.EmitSignal, SignalName.Announcement, e.Text, (int)e.Severity));
    }
}
```

### 2.7 The Command Dispatcher

Player input is **never** a direct method call into simulation internals. Input is expressed as **typed Command objects** — plain C# records with no Godot types:

```csharp
// Core/Commands.cs  (GameLogic project)
public interface ICommand { }

public record DesignateMineCommand     (Vec3i From, Vec3i To)                    : ICommand;
public record PlaceBuildingCommand     (string BuildingId, Vec3i Pos)            : ICommand;
public record AssignLaborCommand       (int DwarfId, string Labor, bool Enabled) : ICommand;
public record SetProductionOrderCommand(int WorkshopId, string RecipeId, int Qty): ICommand;
public record CreateSquadCommand       (string Name)                             : ICommand;
public record ToggleSquadAlertCommand  (int SquadId, bool Active)                : ICommand;
public record SaveGameCommand          (string SlotName)                         : ICommand;
```

```csharp
// Core/CommandDispatcher.cs  (GameLogic project)
public class CommandDispatcher {
    private readonly Dictionary<Type, Action<ICommand>> _handlers = new();

    public void Register<T>(Action<T> handler) where T : ICommand
        => _handlers[typeof(T)] = cmd => handler((T)cmd);

    public void Dispatch(ICommand cmd) {
        if (_handlers.TryGetValue(cmd.GetType(), out var h)) h(cmd);
    }
}
```

Each simulation system registers its handlers during `Initialize`:
```csharp
// In DesignationSystem.Initialize(ctx)
ctx.Commands.Register<DesignateMineCommand>(OnDesignateMine);
ctx.Commands.Register<PlaceBuildingCommand>(OnPlaceBuilding);
```

The Godot `InputHandler` then simply calls:
```csharp
_dispatcher.Dispatch(new DesignateMineCommand(from, to));
```

No simulation code ever changes when the input device or UI layout changes.

### 2.8 GameState Snapshots

The rendering layer **never reads mutable simulation objects directly**. The simulation publishes **read-only snapshot records** each render tick that the Godot layer consumes:

```csharp
// Snapshots/  (GameLogic project — no Godot types)
public sealed record DwarfSnapshot(
    int    Id,
    string Name,
    Vec3i  Position,
    float  HungerPct,
    float  ThirstPct,
    Mood   Mood,
    string CurrentJobDesc,
    bool   IsAlive
);

public sealed record TileViewSnapshot(
    Vec3i  Pos,
    string TileTypeId,
    string MaterialId,
    byte   FluidLevel,
    bool   IsDesignated
);

public sealed record ChunkViewSnapshot(
    Vec3i              ChunkPos,
    TileViewSnapshot[] Tiles    // 16×16×4 = 1024 tiles, pre-flattened
);
```

The `SnapshotSystem` (`IGameSystem`) emits these each tick via the EventBus. The `WorldRenderer` (Godot project) subscribes to `ChunkViewSnapshotEvent` and updates `TileMapLayer` — it never touches `WorldMap` or any entity object directly.

---

## 3. The Three-Layer Model

The entire codebase is divided into exactly three layers. Nothing crosses layer boundaries in the wrong direction.

```
╔══════════════════════════════════════════════════════════════════╗ DwarfFortress.Godot
║  LAYER 3 — PRESENTATION                                          ║ (Godot SDK project)
║  Godot Nodes, TileMapLayers, Scenes, UI Controls                 ║
║  Reads ChunkViewSnapshots + DwarfSnapshots.                      ║
║  Dispatches ICommands. Bridges via GodotEventBridge.             ║
║  Zero game-rule logic. Zero direct simulation object references. ║
╠══════════════════════════╦═══════════════════════════════════════╣
║  GodotEventBridge        ║  CommandDispatcher                    ║ ← Bridge layer
║  C# events → signals     ║  ICommand objects → Systems           ║   (still in .Godot)
╠══════════════════════════╩═══════════════════════════════════════╣ ─────── Project
╠══════════════════════════════════════════════════════════════════╣ boundary
║  LAYER 2 — SIMULATION                                            ║ DwarfFortress.GameLogic
║  Systems: JobSystem, FluidSim, CombatSystem, NeedsSystem…        ║ (plain .NET 8 library)
║  Pure C# — zero Godot references, zero engine types              ║
║  EventBus (C# delegates). Vec3i. ILogger. IDataSource.           ║
║  Publishes Snapshots. Reads definitions from Layer 1.            ║
╠══════════════════════════════════════════════════════════════════╣
║  LAYER 1 — DATA / DEFINITIONS                                    ║
║  JSON files + DataManager (read-only registries)                 ║
║  MaterialDef, ItemDef, CreatureDef, RecipeDef, JobDef…           ║
║  Zero runtime mutation. Loaded once via IDataSource at startup.  ║
╚══════════════════════════════════════════════════════════════════╝
```

**Dependency arrows flow upward only:**
- `DwarfFortress.Godot` depends on `DwarfFortress.GameLogic` (reads snapshots, dispatches commands)
- Layer 2 depends on Layer 1 (looks up definitions to drive logic)
- `DwarfFortress.Tests` depends only on `DwarfFortress.GameLogic` — no Godot required

---

## 4. Content Definition Layer (Data)

### 4.1 Definition vs Instance

Every "thing" in the game has two representations:

| Concept | Definition (Layer 1) | Instance (Layer 2) |
|---|---|---|
| Iron | `MaterialDef { id:"iron", hardness:9, … }` | `TileData { Material:"iron", temp:20 }` |
| Bed | `ItemDef { id:"bed", tags:["furniture","sleep"], … }` | `Item { DefId:"bed", quality:3, wear:0.1 }` |
| Goblin | `CreatureDef { id:"goblin", baseStr:6, … }` | `Creature { DefId:"goblin", hp:24, pos:… }` |
| Mine job | `JobDef { id:"mine", laborType:"Mining", … }` | `Job { DefId:"mine", targetPos:…, ticksDone:12 }` |

Definitions are loaded once into static registries. Instances are created, serialized, and destroyed at runtime.

```csharp
// Definition – immutable, shared, no Godot dependency
public sealed record MaterialDef(
    string Id,
    string Category,
    int Hardness,
    float Density,
    int Value,
    float IgnitionPoint,   // float.MaxValue = non-flammable
    bool IsFluid,
    string[] Tags
);

// Instance – mutable runtime state
public struct MaterialInstance {
    public string  DefId;
    public float   Temperature;
    public byte    Contamination;   // 0 = clean
}
```

### 4.2 The Tag System

Tags are the **primary mechanism for flexible content relationships**. Every definition carries a `string[]` of tags. Systems query by tag, not by specific ID.

**Tag convention:**
```
[category]   – stone, metal, wood, food, drink, fabric, gem, bone, raw, refined, processed
[state]      – raw, refined, processed, cooked, brewed, smelted, cut, decorated
[use]        – fuel, construction, weapon, armour, furniture, container, tool, trade
[property]   – flammable, magnetic, precious, edible, potable, toxic, magical
[body]       – organic, mineral, crystalline
[faction]    – dwarven, goblin, elven, human, neutral
```

**Example: iron bar**
```json
"iron_bar": {
  "tags": ["metal", "refined", "construction", "fuel_compatible"]
}
```

**Example: recipe using tags instead of specific IDs**
```json
"inputs": [
  { "tag": "metal+refined", "quantity": 2 },
  { "tag": "fuel",          "quantity": 1 }
]
```
This recipe now works with iron, copper, bronze, steel, or any future metal marked `[metal][refined]`, without any code change.

**Tag matching rules:**
- `tag: "metal"` → matches anything with the `metal` tag
- `tag: "metal+refined"` → must have BOTH tags (AND)
- `tag: "metal|wood"` → must have EITHER tag (OR)
- `tag: "!goblin"` → must NOT have the `goblin` tag

### 4.3 Effect & Modifier Blocks

Instead of hardcoding what eating food does, or what a buff does, effects are data blocks:

```json
"effect_blocks": [
  { "target": "need.hunger",    "op": "add",         "value": 40 },
  { "target": "need.thirst",    "op": "add",         "value": 15 },
  { "target": "thought",        "op": "add_thought",  "thoughtId": "ate_fine_meal" },
  { "target": "stat.happiness", "op": "add_temp",     "value": 5, "duration_ticks": 2000 }
]
```

The `EffectApplicator` system reads these blocks and mutates the target entity. Adding a new effect type (e.g. a magic potion effect) only requires:
1. A new `op` string in the data
2. A handler registered in `EffectApplicator.RegisterHandler(opName, handler)`

No existing code changes.

### 4.4 Content Registries

`DataManager` is a plain C# class (no Godot inheritance) constructed and injected by `GameBootstrapper`. It exposes typed read-only registries:

```csharp
public sealed class DataManager : IGameSystem {
    public static Registry<MaterialDef>  Materials  { get; private set; }
    public static Registry<ItemDef>      Items      { get; private set; }
    public static Registry<RecipeDef>    Recipes    { get; private set; }
    public static Registry<CreatureDef>  Creatures  { get; private set; }
    public static Registry<JobDef>       Jobs       { get; private set; }
    public static Registry<BuildingDef>  Buildings  { get; private set; }
    public static Registry<BiomeDef>     Biomes     { get; private set; }
    public static Registry<TraitDef>     Traits     { get; private set; }
    public static Registry<ThoughtDef>   Thoughts   { get; private set; }
    public static Registry<EventDef>     WorldEvents{ get; private set; }
}
```

`Registry<T>` is a thin wrapper around `Dictionary<string, T>` with:
- `Get(string id)` — safe lookup, throws descriptive error if missing
- `TryGet(string id, out T def)` — null-safe lookup
- `AllTagged(string tag)` — returns all defs with a given tag
- `AllTaggedAll(string[] tags)` — tags AND filter
- `AllTaggedAny(string[] tags)` — tags OR filter

### 4.5 Cross-References via String IDs

Definitions reference each other only by **string ID**, never by object reference. This decouples load order and allows forward-references.

```json
// workshop_def: "carpenters_workshop"
{
  "allowed_recipes": ["wooden_bed", "wooden_barrel", "wooden_bin"]
}

// recipe_def: "wooden_bed"  — references workshop by string ID
{
  "workshop": "carpenters_workshop",
  "inputs":   [{ "itemId": "log", "qty": 1 }],
  "outputs":  [{ "itemId": "bed", "qty": 1 }]
}
```

At runtime, `RecipeDatabase.GetRecipesForWorkshop("carpenters_workshop")` does the lookup. The recipe doesn't hold a pointer to the workshop.

---

## 5. Simulation Layer (Systems)

### 5.1 System Interface Contract

Every simulation system implements `IGameSystem`:

```csharp
public interface IGameSystem {
    string  SystemId    { get; }         // unique name for diagnostics
    int     UpdateOrder { get; }         // lower = runs earlier each tick
    bool    IsEnabled   { get; set; }

    void    Initialize(GameContext ctx); // called once after all systems loaded
    void    Tick(float delta);           // called each simulation tick
    void    OnSave(SaveWriter w);        // persist state
    void    OnLoad(SaveReader r);        // restore state
}
```

`GameContext` is a lightweight struct passed to each system during `Initialize`, containing handles to all other systems (without creating circular dependencies — systems are loaded in `UpdateOrder` and only interact via EventBus after init).

Systems register themselves via:
```csharp
GameManager.RegisterSystem(new FluidSimulator());
GameManager.RegisterSystem(new NeedsSystem());
GameManager.RegisterSystem(new JobSystem());
// etc.
```

Adding a **new simulation system** means:
1. Create a class implementing `IGameSystem`
2. Register it in the bootstrap — zero modification of existing systems

### 5.2 The EventBus (Decoupled Wiring)

The `EventBus` lives in `DwarfFortress.GameLogic` and uses **pure C# event delegates** — no Godot dependency. The `GodotEventBridge` (Section 2.6) translates relevant events into Godot signals for UI nodes that need them.

```csharp
// EventBus.cs  (GameLogic — pure C#)
public class EventBus {
    private readonly Dictionary<Type, List<Delegate>> _subs = new();

    public void On<T>(Action<T> handler) {
        if (!_subs.TryGetValue(typeof(T), out var list))
            _subs[typeof(T)] = list = new();
        list.Add(handler);
    }

    public void Emit<T>(T ev) {
        if (_subs.TryGetValue(typeof(T), out var list))
            foreach (var d in list) ((Action<T>)d)(ev);
    }
}

// Publishing (any GameLogic system)
EventBus.Emit(new TileChangedEvent(pos, oldTile, newTile));

// Subscribing (during IGameSystem.Initialize)
ctx.EventBus.On<TileChangedEvent>(OnTileChanged);
ctx.EventBus.On<ItemCreatedEvent>(OnItemCreated);
```

**All events are plain C# record structs** with no Godot types — `Vec3i` instead of `Vector3I`:

```csharp
public record struct TileChangedEvent    (Vec3i Pos, TileData Old, TileData New);
public record struct ItemCreatedEvent    (int ItemId, string DefId, Vec3i Pos);
public record struct JobCompletedEvent   (int JobId, int DwarfId);
public record struct DwarfDiedEvent      (int DwarfId, string Cause);
public record struct FluidSpreadEvent    (Vec3i From, Vec3i To, FluidType Fluid);
public record struct CombatHitEvent      (int AttackerId, int DefenderId, BodyPart Part, int Damage);
public record struct WorldEventFiredEvent(string EventDefId, Dictionary<string,object> Context);
```

**Why this matters for extensibility:** A new "contamination spread" system only needs to subscribe to `TileChangedEvent` and `FluidSpreadEvent` — no existing system needs to know it exists, and it can be tested without Godot running.

### 5.3 The Reaction Pipeline

The Reaction Pipeline is the engine for **emergent consequences**. It watches for combinations of conditions and fires reactions. It replaces hardcoded `if (isWater && isMagma)` chains scattered through the codebase.

```
┌──────────────────────────────────────────────────────────────┐
│                    REACTION PIPELINE                         │
│                                                              │
│  EventBus.On<TileChangedEvent>                               │
│      ↓                                                       │
│  For each registered ReactionDef:                            │
│      Check Triggers (tile tags, adjacent tile tags,          │
│                       fluid types, temperature ranges…)      │
│      If all triggers fire:                                   │
│          Execute Effects (create tile, spawn item,           │
│                           emit new event, apply damage…)     │
└──────────────────────────────────────────────────────────────┘
```

Reactions are **data-defined**:

```json
{
  "id": "magma_meets_water",
  "triggers": [
    { "type": "tile_has_fluid",     "fluid": "magma" },
    { "type": "adjacent_has_fluid", "fluid": "water" }
  ],
  "effects": [
    { "type": "set_tile",       "tileType": "obsidian_floor" },
    { "type": "remove_fluid",   "target": "both" },
    { "type": "spawn_gas",      "gasId": "steam", "radius": 2 },
    { "type": "emit_event",     "eventId": "steam_cloud_created" }
  ]
}
```

Adding *magma meets ice* (for a winter dungeon expansion) requires one new JSON reaction entry — no C# changes.

### 5.4 The Modifier Stack

Stats (speed, strength, skill effectiveness) are never set directly. They are computed from a **modifier stack** at read time:

```csharp
public class ModifierStack {
    private readonly List<Modifier> _mods = new();

    public void Add(Modifier mod) => _mods.Add(mod);
    public void Remove(string sourceId) => _mods.RemoveAll(m => m.SourceId == sourceId);

    public float Resolve(float baseValue) {
        float flat    = _mods.Where(m => m.Type == ModType.Flat).Sum(m => m.Value);
        float pctAdd  = _mods.Where(m => m.Type == ModType.PercentAdd).Sum(m => m.Value);
        float pctMult = _mods.Where(m => m.Type == ModType.PercentMult)
                             .Aggregate(1f, (acc, m) => acc * (1 + m.Value));
        return (baseValue + flat) * (1 + pctAdd) * pctMult;
    }
}
```

Every entity attribute runs through a `ModifierStack`. Buffs, debuffs, equipment bonuses, skill bonuses, temperature penalties — all are just `Modifier` objects added to the relevant stack. Adding a new buff to the game is `entity.SpeedStack.Add(new Modifier("haste_potion", ModType.PercentAdd, 0.5f, duration: 3000))` — no special casing.

### 5.5 The Needs/Effects Applicator

The `EffectApplicator` is a registry of effect handlers, each keyed to an `op` string. It receives `ILogger` via the `GameContext` — no Godot dependency:

```csharp
public class EffectApplicator {
    private readonly Dictionary<string, Action<Entity, EffectBlock, GameContext>> _handlers = new();
    private readonly ILogger _logger;

    public EffectApplicator(ILogger logger) => _logger = logger;

    public void RegisterHandler(string op, Action<Entity, EffectBlock, GameContext> handler)
        => _handlers[op] = handler;

    public void Apply(Entity target, EffectBlock[] blocks, GameContext ctx) {
        foreach (var block in blocks) {
            if (_handlers.TryGetValue(block.Op, out var handler))
                handler(target, block, ctx);
            else
                _logger.Error($"Unknown effect op: '{block.Op}' — register a handler in EffectApplicator");
        }
    }
}
```

At startup, each system registers its handlers:
```csharp
applicator.RegisterHandler("add_need",     NeedsSystem.HandleAddNeed);
applicator.RegisterHandler("add_thought",  ThoughtSystem.HandleAddThought);
applicator.RegisterHandler("add_modifier", ModifierSystem.HandleAddModifier);
applicator.RegisterHandler("spawn_item",   ItemSystem.HandleSpawnItem);
// etc.
```

New effect types require only: (1) a JSON op name, (2) one `RegisterHandler` call. Existing handlers never change.

---

## 6. Entity Architecture

### 6.1 Entity Identity & Lifecycle

All runtime entities (dwarves, creatures, items, buildings) share a common identity system:

```csharp
public abstract class Entity {
    public int    Id      { get; }           // auto-incremented, globally unique
    public string DefId   { get; }           // references a Definition
    public bool   IsAlive { get; private set; } = true;

    protected Entity(string defId) {
        Id    = EntityRegistry.NextId();
        DefId = defId;
        EntityRegistry.Register(this);
    }

    public virtual void Destroy() {
        IsAlive = false;
        EntityRegistry.Deregister(Id);
        EventBus.Emit(new EntityDestroyedEvent(Id, DefId));
    }
}
```

`EntityRegistry` is a global `Dictionary<int, Entity>` providing O(1) lookup by ID. This is how all inter-entity references are stored — as `int` IDs, never as object pointers. This makes serialization trivial and prevents dangling references.

### 6.2 Component Bag Pattern

Rather than full ECS or deep inheritance, entities use a **component bag**: a dictionary of named components attached at construction based on the definition's `components` array.

```csharp
public class ComponentBag {
    private readonly Dictionary<Type, IEntityComponent> _components = new();

    public void Add<T>(T component) where T : IEntityComponent
        => _components[typeof(T)] = component;

    public T Get<T>() where T : IEntityComponent
        => (T)_components[typeof(T)];

    public bool Has<T>() where T : IEntityComponent
        => _components.ContainsKey(typeof(T));

    public bool TryGet<T>(out T comp) where T : IEntityComponent {
        if (_components.TryGetValue(typeof(T), out var c)) { comp = (T)c; return true; }
        comp = default; return false;
    }
}
```

A `Dwarf` entity has components: `NeedsComponent`, `MoodComponent`, `SkillsComponent`, `InventoryComponent`, `HealthComponent`, `AIComponent`.

A `Creature` might have: `HealthComponent`, `AIComponent`, `InventoryComponent` (no needs/mood).

A `Building` has: `ProductionComponent`, `StorageComponent`, `HealthComponent`.

**Adding a new component type** (e.g. `MagicComponent` for a future magic update):
1. Create `MagicComponent : IEntityComponent`
2. Register in the entity factory for any def with `"components": ["magic"]`
3. No existing entity class changes

### 6.3 Behaviour Trees for AI

Dwarf and creature AI uses **Behaviour Trees (BT)** — a well-understood, highly extensible AI pattern.

```
Root (Selector)
├── Emergency (Sequence)
│   ├── Condition: IsOnFire
│   └── Action: FleeAndExtinguish
├── Combat (Sequence)
│   ├── Condition: HasEnemy
│   └── Subtree: CombatBehaviour
├── NeedsFirst (Selector)
│   ├── Sequence: [Need.Hunger < 20] → FindFoodAndEat
│   ├── Sequence: [Need.Thirst < 15] → FindDrinkAndDrink
│   └── Sequence: [Need.Sleep < 10]  → FindBedAndSleep
├── DoJob (Sequence)
│   ├── Condition: HasAssignedJob
│   └── Action: ExecuteCurrentJob
├── FindJob (Sequence)
│   ├── Condition: IsIdle
│   └── Action: RequestJobFromSystem
└── Idle (Selector)
    ├── Action: Socialise
    ├── Action: Wander
    └── Action: Rest
```

BT nodes implement `IBTNode`:
```csharp
public interface IBTNode {
    BTStatus Tick(Entity entity, BlackBoard board, GameContext ctx);
}
// BTStatus: Running | Success | Failure
```

**Adding new AI behaviour** (e.g. "pray at shrine" for a religion update):
1. Create `FindAndPrayAction : IBTNode`
2. Add to the dwarf's BT as a new leaf under the Idle selector
3. No existing BT nodes change

The `BlackBoard` is a per-entity key-value store for BT state (target position, cached path, currently held item, etc.). It prevents BT nodes from needing direct references to each other.

### 6.4 Stat & Attribute Derivation Graph

Complex derived stats (e.g. "carry weight capacity" = strength × 5 + armour penalty) are defined as a **directed acyclic graph (DAG)** of formulas, not as hardcoded C# math scattered through the codebase.

```json
"derived_stats": {
  "carry_capacity":   "stat.strength * 5",
  "move_speed":       "stat.agility * 0.8 - equipment.weightPenalty",
  "dodge_chance":     "skill.dodge * 2 + stat.agility - enemy.attackSkill",
  "work_speed_mine":  "skill.mining * 1.5 + stat.strength * 0.5"
}
```

A tiny expression evaluator resolves these at read time (values are cached and invalidated when underlying stats change via the modifier stack). Adding a new derived stat means adding one line to the JSON — player speed, skill bonuses, or formula-driven combat numbers are all expressed the same way.

---

## 7. Job & Action Architecture

### 7.1 Job as Data + Strategy

A `Job` is a **data object** (what needs to be done) paired with a **Strategy** (how to do it):

```csharp
// Data — serializable, storable in the job queue
public class Job {
    public int       Id;
    public string    DefId;           // references JobDef
    public Vector3I  TargetPos;
    public int       TargetEntityId;  // optional
    public int       TicksDone;
    public int       Priority;
    public bool      IsReserved;
    public Dictionary<string, object> Context; // arbitrary per-job data
}

// Strategy — pure logic, not serialized
public interface IJobStrategy {
    string       JobDefId        { get; }
    bool         CanPerform      (Entity worker, Job job, WorldMap map);
    ActionResult PerformStep     (Entity worker, Job job, WorldMap map, float delta);
    void         OnJobAbandoned  (Entity worker, Job job);
    void         OnJobCompleted  (Entity worker, Job job);
}
```

`JobSystem` maps `JobDef.Id → IJobStrategy` at startup:
```csharp
JobSystem.RegisterStrategy(new MineJobStrategy());
JobSystem.RegisterStrategy(new HaulJobStrategy());
JobSystem.RegisterStrategy(new CraftJobStrategy());
```

### 7.2 Action Steps (Sub-Tasks)

Each `IJobStrategy.PerformStep` returns an `ActionResult` that describes one atomic step:

```csharp
public abstract record ActionResult;
public record MoveToward(Vector3I Destination) : ActionResult;
public record PerformAction(int TicksCost, string AnimationId) : ActionResult;
public record WaitFor(int Ticks) : ActionResult;
public record RequireItem(string ItemTag, int Quantity) : ActionResult;
public record JobComplete : ActionResult;
public record JobFailed(string Reason) : ActionResult;
```

This makes job strategies composable and testable. The job runner in `JobSystem` handles all movement, all item fetching, and all animation — strategies just return what they need next.

### 7.3 Preconditions & Postconditions

`JobDef` carries declarative preconditions and postconditions:

```json
{
  "id": "mine_tile",
  "preconditions": [
    { "type": "tile_is", "tileTypes": ["wall"] },
    { "type": "worker_has_labor", "labor": "Mining" },
    { "type": "tile_reachable" }
  ],
  "postconditions": [
    { "type": "set_tile",      "tileType": "floor" },
    { "type": "spawn_item",    "itemFromMaterial": true, "chance": 0.9 },
    { "type": "award_xp",      "skill": "Mining", "amount": 10 }
  ]
}
```

The `JobSystem` evaluates preconditions before creating a job and executes postconditions automatically on completion. Strategies only handle the *in-progress* logic — setup and teardown are declarative.

### 7.4 Adding a New Job Type (walkthrough)

**Example: Add a "Harvest Mushroom" job for underground fungus farms.**

**Step 1 — Add to `jobs.json`:**
```json
"harvest_mushroom": {
  "laborType":  "Farming",
  "skill":      "Farming",
  "priority":   3,
  "preconditions": [
    { "type": "tile_has_tag", "tag": "mushroom_grown" },
    { "type": "worker_has_labor", "labor": "Farming" }
  ],
  "postconditions": [
    { "type": "set_tile_tag",  "removeTag": "mushroom_grown", "addTag": "mushroom_empty" },
    { "type": "spawn_item",    "itemId": "mushroom_cap", "qty_range": [1, 3] },
    { "type": "award_xp",      "skill": "Farming", "amount": 8 }
  ]
}
```

**Step 2 — (Optional) Add a custom strategy only if in-progress behaviour is unique:**
```csharp
public class HarvestMushroomStrategy : IJobStrategy {
    public string JobDefId => "harvest_mushroom";

    public ActionResult PerformStep(Entity worker, Job job, WorldMap map, float delta) {
        // Play crouch animation, then done
        if (job.TicksDone < 60) return new PerformAction(1, "anim_harvest");
        return new JobComplete();
    }
    // ... CanPerform, OnJobAbandoned, OnJobCompleted
}
```

**Step 3 — Register strategy (bootstrap only):**
```csharp
JobSystem.RegisterStrategy(new HarvestMushroomStrategy());
```

**Step 4 — Add the mushroom tile tag to `tiles.json` growth logic.**

That's it. The job queue, assignment, pathfinding, XP system, and UI job log all handle it automatically.

---

## 8. Material & Item Architecture

### 8.1 Material Property Inheritance

Materials support **inheritance** to avoid repeating shared properties:

```json
{
  "id":      "steel",
  "parent":  "metal_base",         // inherits all base metal properties
  "hardness": 11,
  "value":    25,
  "tags":    ["metal", "refined", "ferrous"]
}
```

`metal_base` defines defaults (density, non-flammable, construction-compatible, etc.). `steel` overrides only what differs. The `DataManager` resolves inheritance on load — no runtime cost.

This also drives **fallback logic**. Recipe that says "any `[metal][refined]`" will accept copper, iron, steel, bronze, or any future metal without changes.

### 8.2 Item Template + Runtime Instance

```
ItemDef (JSON template)              Item (runtime instance)
────────────────────────────         ──────────────────────────────────────
id: "war_hammer"                     DefId: "war_hammer"
baseDamage: 12                       Material: "steel"   ← from creation
tags: [weapon, blunt, melee]         Quality: Masterwork ← rolled at craft time
slots: [weapon_hand]                 Wear: 0.05          ← runtime decay
components: [weapon, equippable]     Decorations: ["gem:ruby"] ← added later
```

Item **value** is computed on demand:
```
value = ItemDef.BaseValue × MaterialDef.ValueMultiplier × QualityMultiplier × (1 - Wear) + Decoration.Value
```

All of these multipliers come from data. A new decoration type just adds a new data entry — the value formula handles it automatically.

### 8.3 Adding a New Material (walkthrough)

**Example: Add "mithral" — a rare magical metal.**

**Step 1 — `materials.json`:**
```json
"mithral": {
  "parent":   "metal_base",
  "hardness": 14,
  "density":  1200,
  "value":    500,
  "tags":     ["metal", "refined", "precious", "magical", "lightweight"]
}
```

**Step 2 — `items.json` (optional new items):**
```json
"mithral_chain_shirt": {
  "parent":   "chain_shirt_base",
  "tags":     ["armour", "light", "metal", "magical"]
}
```

**Step 3 — In `recipes.json`, any recipe with `"tag": "metal+refined"` already accepts mithral. Done.**

Mithral will automatically:
- Be hauled to appropriate stockpiles (tagged `[metal]`)
- Be available as input in all metalsmithing recipes
- Display its colour from `MaterialDef.Color` in the renderer
- Have its value factor into trade calculations
- Generate "constructed with mithral" thoughts if used in furniture

---

## 9. Recipe & Production Architecture

### 9.1 Generic Recipe Schema

All production — smelting, cooking, carpentry, alchemy — uses one universal `RecipeDef` schema:

```json
{
  "id":        "smelt_steel",
  "workshop":  "smelter",
  "skill":     "Smelting",
  "baseTicks": 300,

  "inputs": [
    { "itemId": "iron_bar",    "qty": 2 },
    { "itemId": "coal_coke",   "qty": 1, "consumed": true }
  ],

  "outputs": [
    {
      "itemId":    "steel_bar",
      "qty":        1,
      "qualityRoll": true,
      "skillBonus": "Smelting"
    }
  ],

  "byproducts": [
    { "itemId": "slag",  "qty": 1, "chance": 0.3 }
  ],

  "requirements": [
    { "type": "building_present", "buildingTag": "anvil" }
  ],

  "tags": ["metalwork", "basic"]
}
```

### 9.2 Input Matching with Tags

Inputs can use either exact IDs or tag expressions:

```json
"inputs": [
  { "tag":    "metal+ore",  "qty": 2 },   // any ore
  { "tag":    "fuel",       "qty": 1 },   // charcoal OR coal OR coke OR wood
  { "itemId": "bucket",     "qty": 1, "consumed": false }  // tool, not consumed
]
```

`RecipeResolver` checks the stockpile and workshop links for items matching each input. If a recipe says `fuel` and both charcoal and coal are available, it picks the closest/cheapest. Adding a new fuel type just requires tagging its `ItemDef` with `fuel`.

### 9.3 Output Generation Rules

Outputs support conditional generation rules:

```json
"outputs": [
  {
    "itemId": "iron_sword",
    "qty": 1,
    "qualityRoll": true,
    "materialInherited": "input[0]",   // inherits material from first input
    "minQualityForBonus": "Masterwork",
    "ifMasterwork": {
      "addTag": "enchantable"
    }
  }
]
```

This means a legendary weaponsmith automatically produces an enchantable sword — no special code.

### 9.4 Adding a New Workshop + Recipes (walkthrough)

**Example: Add a "Glassworks" workshop that smelts sand into glass items.**

**Step 1 — `buildings.json`:**
```json
"glassworks": {
  "name":         "Glassworks",
  "size":         [3, 3],
  "buildMaterial": "stone",
  "tags":          ["workshop", "heat_producing"],
  "allowedRecipes": ["smelt_glass", "blow_glass_bottle", "blow_glass_lens"]
}
```

**Step 2 — `recipes.json`:**
```json
"smelt_glass": {
  "workshop":  "glassworks",
  "skill":     "Glassmaking",
  "baseTicks": 200,
  "inputs":  [{ "itemId": "sand",  "qty": 3 }, { "tag": "fuel", "qty": 1 }],
  "outputs": [{ "itemId": "glass_bar", "qty": 2, "qualityRoll": true }]
},
"blow_glass_bottle": {
  "workshop":  "glassworks",
  "skill":     "Glassmaking",
  "baseTicks": 150,
  "inputs":  [{ "itemId": "glass_bar", "qty": 1 }],
  "outputs": [{ "itemId": "glass_bottle", "qty": 2 }]
}
```

**Step 3 — `skills.json`:** Add `"Glassmaking"` skill entry.

**Step 4 — `items.json`:** Add `glass_bar`, `glass_bottle` item defs.

**Step 5 — `materials.json`:** Add `"glass"` material.

No C# code written. The glassworks workshop appears in the Build menu automatically (all buildings tagged `[workshop]` are listed), recipes appear in its production queue, dwarves with Glassmaking labor enabled will take jobs, and glass bottles can be used in any recipe accepting `[container]`-tagged items.

---

## 10. Tile & World Architecture

### 10.1 Tile Behaviour via Tile Traits

Rather than a large `switch(TileType)` statement, each tile type references **trait objects** that define its behaviour:

```json
"obsidian_wall": {
  "group":       "stone_wall",
  "material":    "obsidian",
  "traits":      ["mineable", "supports_engraving", "blocks_light", "blocks_movement"],
  "minedResult": { "itemTag": "stone_boulder", "materialInherited": true }
}
```

Traits are registered C# objects:
```csharp
public interface ITileTrait {
    string TraitId { get; }
    void OnTileEntered  (Vector3I pos, Entity entity, WorldMap map);
    void OnTileChanged  (Vector3I pos, TileData old, TileData newTile, WorldMap map);
    void OnFluidArrived (Vector3I pos, FluidType fluid, byte level, WorldMap map);
}
```

`TileTraitRegistry` maps trait IDs to implementations. New tile behaviours = new trait classes registered at startup. Tile definitions just reference the trait string IDs.

### 10.2 Tile Change Propagation

When any tile changes, a **propagation graph** determines what downstream checks to run:

```
TileChanged
    │
    ├── → FluidSimulator.InvalidateAdjacent(pos)
    ├── → LightPropagator.InvalidateFrom(pos)
    ├── → PathfindingGraph.InvalidateNode(pos)
    ├── → RoomDetector.CheckRoomIntegrity(pos)
    ├── → JobSystem.RevalidateJobsAt(pos)
    └── → ReactionPipeline.CheckReactionsAt(pos)
```

Every subscriber receives `TileChangedEvent` from the EventBus and decides independently whether the change matters to it. No tile-change code lives in `WorldMap.cs`.

### 10.3 Adding a New Tile Type (walkthrough)

**Example: Add "crystal formation" — a decorative tile that emits faint light.**

**Step 1 — `tiles.json`:**
```json
"crystal_formation": {
  "group":     "natural_feature",
  "material":  "quartz",
  "traits":    ["mineable", "emits_light", "decorative"],
  "lightEmit": 40,
  "minedResult": { "itemId": "raw_crystal", "qty_range": [1, 3] }
}
```

**Step 2 — Register `emits_light` trait (if not already existing):**
```csharp
public class EmitsLightTrait : ITileTrait {
    public string TraitId => "emits_light";
    public void OnTileChanged(Vector3I pos, TileData old, TileData newTile, WorldMap map) {
        var lightVal = (byte)(newTile.Def.TryGetValue("lightEmit", out var v) ? v : 0);
        map.SetTileLight(pos, lightVal);
        EventBus.Emit(new LightSourceChangedEvent(pos));
    }
    // other methods: no-op
}
TileTraitRegistry.Register(new EmitsLightTrait());
```

**Step 3 — Add tileset graphics** for the crystal tile.

Done. Crystals appear in world gen (add to `biomes.json` feature list), emit light correctly, can be mined, their item drops go to gem stockpiles (tagged `[gem]`), and they generate thoughts ("I saw a beautiful crystal formation") if configured in `thoughts.json`.

---

## 11. Creature Architecture

### 11.1 Creature Definition Schema

```json
{
  "id":          "cave_spider",
  "name":        "Cave Spider",
  "tags":        ["creature", "arthropod", "hostile_wild", "venomous", "underground"],
  "size":        2,
  "speed":       1.4,
  "attributes":  { "strength": 3, "agility": 8, "toughness": 2 },
  "bodyParts":   ["head", "thorax", "abdomen", "leg×8", "fang×2"],
  "naturalAttacks": [
    { "bodyPart": "fang", "type": "pierce", "damage": 4,
      "onHit": [{ "type": "apply_status", "statusId": "venom_minor" }] }
  ],
  "components":  ["health", "ai", "inventory"],
  "ai": {
    "behaviourModule": "predator",
    "aggroTags":       ["dwarf", "animal_small"],
    "fleeThreshold":   0.15,
    "territorRadius":  12
  },
  "drops": [
    { "itemId": "spider_silk", "chance": 0.7, "qty_range": [2, 5] }
  ],
  "lairTags": ["cave", "dark", "underground"]
}
```

### 11.2 Creature AI via Behaviour Modules

Rather than one monolithic creature AI, creatures reference named **behaviour modules**:

| Module ID | Behaviour |
|---|---|
| `passive_grazer` | Wanders, flees all threats |
| `predator` | Hunts tagged prey within detection range |
| `territorial` | Attacks anything entering lair radius |
| `pack_hunter` | Coordinates attack with nearby same-species |
| `siege_attacker` | Path-finds to fortress walls, attacks doors |
| `megabeast` | Seeks dwarves/buildings, extremely aggressive, intelligent |
| `undead` | Attacks everything, ignores morale, does not flee |

Modules are `IBehaviourModule` implementations registered at startup. A creature's `ai.behaviourModule` field picks which module drives its BT root node.

**Adding a new creature behaviour** (e.g. carrion scavenger that eats corpses):
1. Create `ScavengerModule : IBehaviourModule`
2. Register it
3. Reference it in any creature def: `"behaviourModule": "scavenger"`

### 11.3 Adding a New Creature (walkthrough)

**Example: Add a "Blind Cave Fish" — passive, swims in underground lakes.**

**`creatures.json`:**
```json
"blind_cave_fish": {
  "tags":       ["creature", "fish", "passive", "aquatic", "underground"],
  "size":       1,
  "speed":      0.8,
  "attributes": { "strength": 1, "agility": 6, "toughness": 1 },
  "components": ["health", "ai"],
  "ai": {
    "behaviourModule": "passive_grazer",
    "movementType":    "swim",
    "requiredTile":    "water"
  },
  "drops": [{ "itemId": "raw_fish", "chance": 1.0, "qty_range": [1, 2] }]
}
```

Add `"blind_cave_fish"` to the biome's underground lake feature list. Done. The fish spawns in underground water tiles, swims passively, can be caught with a fishing zone, drops raw fish which goes to the food stockpile — all via existing systems.

---

## 12. World Event Architecture

### 12.1 Event Definition Schema

```json
{
  "id":          "goblin_ambush",
  "category":    "threat",
  "name":        "Goblin Ambush",
  "tags":        ["hostile", "goblin", "surface"],

  "triggerConditions": [
    { "type": "fortress_year_min",     "value": 1 },
    { "type": "fortress_wealth_min",   "value": 5000 },
    { "type": "cooldown_elapsed",      "value": 90 }
  ],

  "spawnRules": [
    {
      "creatureTag":   "goblin",
      "countRange":    [3, 8],
      "equipmentTier": "basic",
      "spawnEdge":     "random"
    }
  ],

  "onArrival": [
    { "type": "announce",  "message": "A goblin ambush has arrived!", "severity": "danger" },
    { "type": "auto_pause_suggestion" }
  ],

  "scalingRules": {
    "wealthFactor":      0.001,
    "yearFactor":        1.2,
    "maxCountMultiplier": 5.0
  }
}
```

### 12.2 Trigger Conditions

`WorldEventManager` evaluates each event's `triggerConditions` each in-game season. Conditions are composable:

```csharp
public interface IEventCondition {
    string ConditionType { get; }
    bool   Evaluate(WorldState state, EventDef def);
}
```

Built-in conditions: `fortress_year_min`, `fortress_wealth_min`, `fortress_population_min`, `season_is`, `cooldown_elapsed`, `item_stockpiled_min`, `building_exists`, `previous_event_fired`.

New conditions (e.g. "elves are angry") = one new `IEventCondition` class.

### 12.3 Event Chains & Consequences

Events can trigger follow-up events by emitting `WorldEventFiredEvent`. Other event definitions listen for this:

```json
{
  "id": "goblin_siege",
  "triggerConditions": [
    { "type": "previous_event_fired", "eventId": "goblin_ambush", "timesMin": 3 },
    { "type": "fortress_year_min",    "value": 3 }
  ]
}
```

This creates **emergent escalation chains** without hardcoded logic. A modder can create a completely new event chain (e.g. cultist infiltrators → ritual sacrifice → demon summoning) entirely in data.

### 12.4 Adding a New World Event (walkthrough)

**Example: Add "Plague of Flies" — a seasonal annoyance that lowers comfort needs.**

**`events.json`:**
```json
"plague_of_flies": {
  "category": "nuisance",
  "tags":     ["natural", "seasonal", "surface"],
  "triggerConditions": [
    { "type": "season_is",    "season": "Summer" },
    { "type": "fortress_year_min", "value": 1 },
    { "type": "cooldown_elapsed",  "value": 200 }
  ],
  "effects": [
    { "type": "apply_global_modifier",
      "modifierTarget": "need.comfort",
      "op":             "percent_add",
      "value":          -0.3,
      "duration_days":  14 },
    { "type": "announce",
      "message": "Clouds of flies swarm the surface. Dwarves are uncomfortable.",
      "severity": "info" },
    { "type": "add_global_thought",
      "thoughtId": "flies_annoying",
      "affectedTag": "surface_worker" }
  ]
}
```

**`thoughts.json`:** Add `"flies_annoying": { "happiness": -3, "duration_days": 7, "text": "was pestered by flies" }`.

Zero C# written.

---

## 13. Inter-System Communication Map

This diagram shows which systems produce events and which consume them, revealing every cross-system dependency:

```
PRODUCERS                   EVENT                        CONSUMERS
──────────────────────────────────────────────────────────────────────────
WorldMap             →  TileChangedEvent          → FluidSim, Lighting,
                                                     Pathfinding, RoomDetector,
                                                     ReactionPipeline, JobSystem

ItemSystem           →  ItemCreatedEvent           → StockpileManager, UIManager
                    →  ItemDestroyedEvent          → StockpileManager

JobSystem            →  JobCreatedEvent            → UIManager (log)
                    →  JobCompletedEvent           → XPSystem, StockpileManager,
                                                     UIManager

NeedsSystem          →  NeedCriticalEvent          → JobSystem (create eat/drink/sleep job),
                                                     AdvisorSystem, UIManager

MoodSystem           →  MoodChangedEvent           → UIManager, AdvisorSystem
                    →  MoodBreakEvent              → JobSystem, MilitaryManager, UIManager

DwarfEntity          →  DwarfDiedEvent             → RelationshipSystem (grief thoughts),
                                                     MilitaryManager, UIManager, EventLog

CombatSystem         →  CombatHitEvent             → HealthSystem, BloodSystem,
                                                     MoodSystem (witnessing)
                    →  CombatEndedEvent            → MilitaryManager, EventLog

WorldEventManager    →  WorldEventFiredEvent       → UIManager (announce), 
                                                     EventLog, other Events (chains),
                                                     AdvisorSystem

FluidSimulator       →  FluidSpreadEvent           → ReactionPipeline, HealthSystem,
                                                     TileRenderer
                    →  FloodWarningEvent           → UIManager

TimeManager          →  OnDayPassed                → NeedsSystem, FarmingSystem,
                                                     WeatherSystem, AutoSaveSystem
                    →  OnSeasonChanged             → WorldEventManager, FarmingSystem,
                                                     WeatherSystem, MigrantScheduler
                    →  OnYearPassed                → WorldEventManager

CaravanEvent         →  CaravanArrivedEvent        → UIManager, JobSystem (haul to depot)
                    →  CaravanDepartedEvent        → DiplomacySystem, EventLog
```

Any new system slots in by subscribing to the relevant events. **Nothing in any existing producer changes.**

---

## 14. Dependency & Initialization Order

Initialisation is split into two phases: the **GameLogic bootstrap** (pure C#, no Godot) and the **Godot wiring** (connects rendering and input after the simulation is ready).

### Phase 1 — GameLogic Bootstrap (`GameSimulation.Initialize()`)

All `IGameSystem` implementations in `DwarfFortress.GameLogic`, ordered by their `UpdateOrder` integer:

| Order | System | Depends On |
|---|---|---|
| 0 | `DataManager` | `IDataSource` — loads JSON via injected interface |
| 1 | `EntityRegistry` | DataManager |
| 2 | `WorldMap` | DataManager, EntityRegistry |
| 3 | `ItemSystem` | WorldMap, DataManager |
| 4 | `StockpileManager` | ItemSystem, WorldMap |
| 5 | `NeedsSystem` | DataManager, EntityRegistry |
| 6 | `ThoughtSystem` | DataManager, EntityRegistry |
| 7 | `MoodSystem` | NeedsSystem, ThoughtSystem |
| 8 | `SkillSystem` | DataManager, EntityRegistry |
| 9 | `HealthSystem` | EntityRegistry |
| 10 | `JobSystem` | WorldMap, StockpileManager, EntityRegistry |
| 11 | `DesignationSystem` | WorldMap, JobSystem — registers `DesignateMineCommand` etc. |
| 12 | `RecipeSystem` | DataManager, StockpileManager, JobSystem |
| 13 | `FluidSimulator` | WorldMap |
| 14 | `LightPropagator` | WorldMap |
| 15 | `ReactionPipeline` | WorldMap, FluidSimulator, DataManager |
| 16 | `CombatSystem` | EntityRegistry, HealthSystem |
| 17 | `MilitaryManager` | CombatSystem, JobSystem, StockpileManager |
| 18 | `WorldEventManager` | DataManager, TimeManager |
| 19 | `AdvisorSystem` | EventBus (subscribes to all warning events) |
| 20 | `SnapshotSystem` | WorldMap, EntityRegistry — emits view snapshots each tick |
| 21 | `SaveSystem` | All above |

New systems pick a number after their dependencies. The bootstrap never needs reordering for any other reason.

### Phase 2 — Godot Layer Wiring (`GameBootstrapper._Ready()`)

After `GameSimulation.Initialize()` returns, the Godot project connects itself to the running simulation:

| Step | Action | Who |
|---|---|---|
| 1 | `GodotEventBridge.Attach(sim.EventBus)` | Connects C# events → Godot signals for UI |
| 2 | `InputHandler.Attach(sim.CommandDispatcher)` | Connects player input → ICommand dispatch |
| 3 | `WorldRenderer` subscribes to `ChunkViewSnapshotEvent` | Rendering driven by snapshots |
| 4 | `DwarfPanel` subscribes to `DwarfSnapshotEvent` | UI driven by snapshots |
| 5 | `AnnouncementLog` subscribes to `AnnouncementEvent` | Log driven by events |

The Godot layer is stateless with respect to the simulation — it only reads what is pushed to it.

---

## 15. Mod & Extension Loading

The data-driven architecture is 90% of mod support. The remaining 10% is file discovery:

### File Discovery

`DataManager` scans in order:
1. `res://data/` — base game data (read-only)
2. `user://mods/*/data/` — installed mods, loaded alphabetically
3. Registered mod directories (future: Steam Workshop paths)

Mods can:
- **Add** new entries to any registry (new material, new creature, new recipe)
- **Override** existing entries by using the same `id` (e.g. rebalance iron's hardness)
- **Extend** existing entries by using a special `_extend` key (add a new recipe to an existing workshop without replacing all its recipes)

```json
// mod file: mymod/data/workshops.json
{
  "_extend": "carpenters_workshop",
  "allowed_recipes": ["wooden_shield", "wooden_staff"]
}
```

This appended list is **merged** with the base game's list at load time.

### C# Mod Hooks

For mods that need custom behaviour (not just data), a compiled `.dll` placed in `user://mods/mymod/` is loaded via `Assembly.LoadFrom()`. The DLL must implement `IModPlugin`:

```csharp
public interface IModPlugin {
    string ModId      { get; }
    string ModVersion { get; }
    void   OnLoad     (GameContext ctx);  // register strategies, traits, conditions, etc.
}
```

Security note: mod DLLs run with full trust (desktop game assumption). A sandboxed scripting layer (e.g. Lua via a NuGet binding) is the future path for untrusted mods.

---

## 16. Testing Strategy

`DwarfFortress.Tests` references only `DwarfFortress.GameLogic`. It has **no Godot SDK reference**. Running `dotnet test` from the CLI requires no Godot installation.

### Unit Tests (pure .NET — no Godot, no file I/O)

```csharp
// Test that the RecipeSystem resolves material tags correctly
[Fact]
public void Recipe_AcceptsAnyRefinedMetal() {
    var registry = new Registry<MaterialDef>();
    registry.Add(new MaterialDef("iron",   Tags: ["metal","refined"]));
    registry.Add(new MaterialDef("copper", Tags: ["metal","refined"]));

    var recipe    = RecipeDef.Parse("{ inputs: [{ tag:'metal+refined', qty:1 }] }");
    var ironBar   = new Item("iron_bar",   material: "iron");
    var copperBar = new Item("copper_bar", material: "copper");

    Assert.True(RecipeResolver.InputMatches(recipe.Inputs[0], ironBar,   registry));
    Assert.True(RecipeResolver.InputMatches(recipe.Inputs[0], copperBar, registry));
}
```

### Integration Tests (pure .NET — no Godot)

Construct a full `GameSimulation` using test doubles. No Godot scene, no window, no GPU:

```csharp
[Fact]
public async Task MineTile_ProducesBoulder_AndUpdatesMap() {
    // Arrange — build sim with in-memory fakes
    var dataSource = new InMemoryDataSource();
    dataSource.AddFile("data/tiles.json",     TileFixtures.BasicJson);
    dataSource.AddFile("data/materials.json", MaterialFixtures.BasicJson);
    var logger = new TestLogger();

    var sim = new GameSimulation(logger, dataSource);
    sim.RegisterDefaultSystems();
    sim.Initialize();

    var map = sim.Context.Get<WorldMap>();
    var pos = new Vec3i(5, 5, 3);
    map.SetTile(pos, TileFactory.GraniteWall());

    var capturedItems = new List<ItemCreatedEvent>();
    sim.EventBus.On<ItemCreatedEvent>(capturedItems.Add);

    // Act — dispatch a command, step the simulation
    sim.CommandDispatcher.Dispatch(new DesignateMineCommand(pos, pos));
    var dwarf = DwarfFactory.CreateWithLabor(sim.Context, "Mining");
    SimulationStepper.RunUntilJobComplete(sim, dwarf, timeoutTicks: 500);

    // Assert
    Assert.Equal(TileType.Floor, map.GetTile(pos).Type);
    Assert.Contains(capturedItems, e => e.DefId.Contains("boulder"));
    Assert.Empty(logger.Errors); // no unknown effect ops or missing handlers
}
```

### Console Runner (headless profiling & debugging)

`ConsoleGameRunner` in `DwarfFortress.Tests` boots a full simulation to benchmark performance and debug emergent behaviour without opening Godot:

```csharp
// ConsoleRunner/ConsoleGameRunner.cs
public static class ConsoleGameRunner {
    public static void Main(string[] args) {
        var sim = new GameSimulation(
            new TestLogger(),
            new InMemoryDataSource().LoadDirectory("../../data/")
        );
        sim.RegisterDefaultSystems();
        sim.Initialize();

        // Generate a world and run for N years
        sim.CommandDispatcher.Dispatch(new GenerateWorldCommand(seed: 42, size: 48));

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 24_000; i++)   // ~100 in-game days at 20 ticks/day
            sim.Tick(delta: 0.05f);

        Console.WriteLine($"Simulated 100 days in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Dwarves alive: {sim.Context.Get<EntityRegistry>().CountAlive<Dwarf>()}");
    }
}
```

### Key Testability Enablers

- `GameSimulation` is a plain C# class — constructable with `new` in any test
- `DwarfFortress.Tests` has **zero Godot assembly references** — `dotnet test` works standalone
- `EventBus` is pure C# — `TestEventBus` variants can capture events synchronously for assertions
- `InMemoryDataSource` seeds any data inline; no JSON files on disk required for unit tests
- `WorldMap` can be created programmatically (`WorldMapFactory.CreateTestMap`) without running world generation
- `TimeManager` is driven by `Tick(delta)` calls — tests step time discretely and deterministically
- `ILogger` injection means test assertions can check for unexpected error log output

---

## 17. Architecture Anti-Patterns to Avoid

These are the pitfalls that make games like DF hard to extend — we explicitly avoid them:

| Anti-Pattern | Why Bad | Our Solution |
|---|---|---|
| `switch(TileType)` chains | Adding a tile requires editing every switch statement | Tile traits registered per type |
| `if (itemId == "iron_bar")` in recipes | Hardcodes specific items | Tag-based recipe inputs |
| Direct system references (`JobSystem.fluidsim.tiles[x,y,z]`) | Creates circular dependencies, untestable | EventBus + GameContext handles only |
| Inheritance chains for creatures / items | Brittle, can't combine features | Component bag + data definitions |
| Storing object references in save data | Reference breaks on load | Store IDs only, resolve on load |
| Hard-coded event sequences (`if (year > 3) SpawnSiege()`) | Can't be modded, can't be extended | Declarative event trigger conditions |
| Constants in C# for game balance numbers | Requires recompile to tune | All numeric constants in JSON |
| Monolithic `Dwarf.cs` doing everything | Impossible to extend without merge conflicts | Components + BT nodes + EffectBlocks |
| Single `Update()` calling all subsystems | Ordering bugs, no parallelism path | `IGameSystem` with `UpdateOrder`, each owns its update |
| UI directly reading simulation structs | Couples rendering to simulation internals | UI reads only from Snapshot records emitted by `SnapshotSystem` |
| Godot types (`Vector3I`, `Node`) in simulation | Can't unit test without Godot; engine lock-in | `Vec3i`, plain C# classes, `ILogger`, `IDataSource` |
| Calling simulation methods from Godot `_Input()` directly | Tight coupling; hard to replay, record, or bot | All input goes through `CommandDispatcher.Dispatch(ICommand)` |
| `using Godot;` in a GameLogic file | Breaks test-without-Godot requirement | Enforce via `.csproj` — GameLogic has no Godot SDK reference |

---

*Architecture document version 2.0 — March 2026*
*Companion to: PLAN.md*

> **v2.0 changes:** Introduced multi-project solution structure (`GameLogic` / `Godot` / `Tests`). Added engine-agnostic `Vec3i`, `ILogger`, `IDataSource`. Replaced Godot-signal EventBus with pure C# delegate bus + `GodotEventBridge`. Added `CommandDispatcher`, `GameState Snapshots`, `GameSimulation` entry point, `GodotEventBridge`, console runner, and two-phase initialization order. Removed all `GD.*` references from GameLogic.
