# Dwarf Fortress Clone — Systems Reference

This document describes every simulation system, what it reads and writes,
events it emits/consumes, and which cross-system chains produce **emergent behavior**.

---

## System Execution Order

| Order | System | Description |
|---|---|---|
| 0  | DataManager | Loads all JSON defs; seals registries |
| 1  | TimeSystem | Advances clock; fires Day/Season/Year events |
| 2  | WorldMap | Tile CRUD; pathfinding integration point |
| 3  | EntityRegistry | Entity lifecycle; alive/dead tracking |
| 4  | ItemSystem | Spawns, destroys, moves items in the world |
| 5  | NeedsSystem | Decays hunger/thirst/sleep; fires critical events |
| 6  | ThoughtSystem | Reacts to events by adding/expiring thoughts |
| 7  | MoodSystem | Sums thoughts → happiness → Mood enum |
| 8  | SkillSystem | Awards XP on job completion; fires level-up events |
| 9  | HealthSystem | Ticks wounds/bleeding; kills entities at 0 HP |
| 10 | JobSystem | Assigns jobs to dwarves; runs strategies |
| 11 | ContaminationSystem | Coating pickup/transfer/ingestion pipeline |
| 12 | BehaviorSystem | Autonomous idle behaviors (groom, socialize, tantrum) |
| 13 | EffectApplicator | Pure-utility: applies EffectBlock ops to entities |
| 14 | ReactionPipeline | Data-driven trigger→effect rules evaluated each tick |
| 15 | RecipeSystem | Production queue; crafting job execution |
| 16 | FluidSimulator | CA-based fluid spreading; fires FloodedTileEvent |
| 17 | CombatSystem | Hostile creature attacks; melee resolution |
| 18 | MilitaryManager | Squads, orders, patrol routes |
| 19 | StockpileManager | Zone management; item routing via haul jobs |
| 20 | WorldEventManager | Season/day triggered world events from data |
| 21 | SnapshotSystem | Builds read-only DTOs for the Godot renderer |
| 22 | SaveSystem | Serializes/deserializes simulation state |

---

## System Contracts

### DataManager
- **Reads**: IDataSource files (data/materials, tiles, items, jobs, creatures, recipes, reactions, world_events, buildings)
- **Writes**: Sealed registries: Materials, Tiles, Items, Jobs, Creatures, Recipes, Reactions, WorldEvents, Buildings
- **Emits**: nothing
- **Emergent role**: The quality of data determines ALL possible emergent scenarios. Every new interaction is just new JSON.

---

### TimeSystem
- **Reads**: delta
- **Writes**: Day, Hour, Month, Year, Season
- **Emits**: `DayStartedEvent`, `SeasonChangedEvent`, `YearPassedEvent`
- **Consumed by**: WorldEventManager (season/day triggers), NeedsSystem (could add seasonal need rate changes), ThoughtSystem

---

### NeedsSystem
- **Reads**: `NeedsComponent` on all alive Dwarves
- **Writes**: `Need.Level` (decay per tick)
- **Emits**: `NeedCriticalEvent(DwarfId, NeedId)`
- **Consumes**: nothing
- **Emergent chains**:
  - `NeedCriticalEvent(hunger)` → **ThoughtSystem** adds `thought_hungry` → **MoodSystem** drops happiness → **BehaviorSystem** may trigger tantrum if Mood = Sufferer
  - `NeedCriticalEvent(thirst)` → **JobSystem** creates eat/drink job → if item unavailable, job fails → mood drops further
  - Combined hunger + thirst + sleep → cascading mood collapse → tantrum → property destruction → other dwarves get `thought_comrade_died`

---

### ThoughtSystem
- **Reads**: Events on bus
- **Writes**: `ThoughtComponent` on affected Dwarves
- **Emits**: `ThoughtAddedEvent(DwarfId, ThoughtId, HappinessMod)`
- **Consumes**: `JobCompletedEvent`, `NeedCriticalEvent`, `EntityKilledEvent`, `SkillLeveledUpEvent`, `SubstanceEffectEvent` *(new)*
- **Emergent chains**:
  - Skill level-up → positive thought → better mood → more productive work
  - `thought_tipsy` (from alcohol) gives +happiness but -Agility → combat disadvantage + happier dwarf
  - `thought_comrade_died` spreads grief; if multiple deaths in short period, cascade to fort-wide misery

---

### MoodSystem
- **Reads**: `ThoughtComponent.TotalHappiness` per Dwarf
- **Writes**: `MoodComponent.Current`, `MoodComponent.Happiness`
- **Emits**: `MoodChangedEvent(DwarfId, OldMood, NewMood)`
- **Consumed by**: **BehaviorSystem** (triggers tantrum/berserk at Sufferer)
- **Emergent chains**:
  - Mood = Sufferer → BehaviorSystem fires `TantrumBehavior` → damages items → ItemSystem destroys items → fewer resources → more unmet needs → deeper spiral

---

### SkillSystem
- **Reads**: `JobCompletedEvent`
- **Writes**: `SkillComponent` XP/Level on Dwarves
- **Emits**: `SkillLeveledUpEvent`
- **Emergent chains**:
  - Higher mining skill → JobSystem strategy finishes jobs faster (work time modifier) → more ore → more crafting → more items → needs more easily met → happier dwarves
  - Skill level-up → ThoughtSystem adds positive thought → MoodSystem improves happiness

---

### HealthSystem
- **Reads**: `HealthComponent` on all alive entities
- **Writes**: `HealthComponent.CurrentHealth` (bleeding decay)
- **Emits**: `DwarfDiedEvent`, `DwarfWoundedEvent`
- **Consumed by**: **ThoughtSystem** (`thought_comrade_died`), **MilitaryManager**
- **Emergent chains**:
  - Bleeding wound over time → death → `DwarfDiedEvent` → ThoughtSystem adds grief to ALL dwarves → fort-wide mood drop

---

### JobSystem
- **Reads**: designated tiles, available dwarves + labors, job queue
- **Writes**: `Job` state (Pending → InProgress → Completed/Failed/Cancelled)
- **Emits**: `JobCreatedEvent`, `JobCompletedEvent`, `JobFailedEvent`, `JobCancelledEvent`
- **Consumed by**: SkillSystem, ThoughtSystem, DesignationSystem
- **Emergent chains**:
  - MineTileStrategy → emits `TileChangedEvent` → FluidSimulator activates if fluid nearby → potential flood
  - HaulItemStrategy → moves food/drink to stockpile → satisfies NeedsSystem → prevents critical events
  - Job failure (no path) → NeedCriticalEvent never cleared → dwarf deteriorates

---

### ContaminationSystem *(new)*
- **Reads**: `PositionComponent` on entities, `TileData.CoatingMaterialId`, `TileData.FluidMaterialId`
- **Writes**: `BodyPartComponent` coatings, `BodyChemistryComponent` concentrations
- **Emits**: `CoatingPickedUpEvent(EntityId, BodyPartId, MaterialId)`, `SubstanceIngestedEvent(EntityId, SubstanceId, Amount)`
- **Consumes**: `TileChangedEvent` (for fluid-dries → coating conversion)
- **Emergent chains** (the cat bug class):
  - Fluid (beer) dries on tile → `TileData.CoatingMaterialId = "beer"` → cat walks through → coating on paws → `BehaviorSystem.GroomingBehavior` fires → substance ingested → `BodyChemistryComponent["alcohol"] += 0.3` → `ReactionPipeline` trigger `entity_has_substance(alcohol, >0.2)` fires → EffectApplicator applies `add_modifier(agility, -0.2)` + `add_thought(thought_tipsy, +0.15)` → cat is drunk
  - Dwarf steps in magma coating → `add_substance(heat)` → HealthSystem damage reaction fires
  - Dwarf in mud-coated tile → `add_modifier(speed, -0.3)` slow debuff from reaction def

---

### BehaviorSystem *(new)*
- **Reads**: `MoodComponent`, `NeedsComponent`, `BodyPartComponent`; entity position
- **Writes**: `NeedsComponent` (social/recreation satisfaction), `BodyChemistryComponent` (via ContaminationSystem events)
- **Emits**: `BehaviorFiredEvent(EntityId, BehaviorId)`
- **Consumes**: `MoodChangedEvent`, `NeedCriticalEvent`
- **Built-in behaviors**:
  - **GroomingBehavior**: periodically fires for creatures with `groomer` tag; triggers ingestion of body part coatings via ContaminationSystem
  - **SocializeBehavior**: fires when Social need < 0.4; satisfies social need if another dwarf is adjacent; adds `thought_socialized`
  - **TantrumBehavior**: fires when Mood = Sufferer + !HasSnapped; randomly destroys adjacent items, may attack nearby dwarves (via CombatSystem), sets `HasSnapped = true`
  - **WanderBehavior**: fires for creatures without an active job; moves to a random adjacent passable tile
- **Emergent chains**:
  - TantrumBehavior → CombatSystem.AttackEntity → HealthSystem wound → DwarfDiedEvent → ThoughtSystem grief → more tantrums → cascade
  - SocializeBehavior → both dwarves satisfy Social need → thoughts improve → mood improves → break the spiral
  - GroomingBehavior → ContaminationSystem.IngestCoating → BodyChemistry substance → ReactionPipeline fires

---

### BodyChemistryComponent *(new, component)*
- Tracks named substance concentrations (0..n, unbounded: can accumulate)
- `DecayAll(delta)` metabolizes over time (substance-specific rate)
- **Interacts with**:
  - ContaminationSystem: writes concentrations on ingestion/contact
  - ReactionPipeline: trigger type `entity_has_substance(id, >, threshold)` reads concentrations
  - EffectApplicator: op `add_substance(id, amount)` writes concentrations
  - ThoughtSystem: `SubstanceEffectEvent` causes thoughts (tipsy, nauseous, etc.)

---

### EffectApplicator
- **Pure utility**: no Tick logic; called by ReactionPipeline, WorldEventManager, BehaviorSystem
- **Ops**: `damage`, `heal`, `add_modifier`, `add_thought`, `satisfy_need`, `add_substance` *(new)*
- **Emergent role**: Everything that changes entity state goes through this — it's the universal effect bus

---

### ReactionPipeline
- **Reads**: `ReactionDef` list from DataManager; entity state each tick
- **Consumes**: nothing (polls entities directly)
- **Emits**: `ReactionFiredEvent`
- **Trigger types**: `entity_has_tag`, `need_critical`, `entity_has_substance` *(new)*, `body_part_has_coating` *(new)*
- **Emergent role**: Pure data bridge. ANY new interaction is a new `reactions/core.json` entry. No code change needed.
- **Emergent chains** (all data-configurable):
  - `entity_has_substance(alcohol, > 0.2)` → `add_modifier(agility, -0.2)` + `add_thought(tipsy)`
  - `entity_has_substance(alcohol, > 0.8)` → `add_modifier(agility, -0.6)` + `add_modifier(speed, -0.4)` + `satisfy_need(recreation, 0.5)` (very drunk but happy)
  - `entity_has_substance(magma_heat, > 0.1)` → `damage(amount=50)` + `add_wound(legs, Critical)`
  - `body_part_has_coating(feet, mud)` → `add_modifier(speed, -0.3, duration=60)`
  - `need_critical(hunger)` → `add_thought(thought_starving, -0.4)`

---

### FluidSimulator
- **Reads/Writes**: `TileData.FluidType`, `TileData.FluidLevel`, `TileData.FluidMaterialId` *(new)*
- **Emits**: `FloodedTileEvent`
- **Consumes**: `TileChangedEvent`
- **Emergent chains**:
  - Mining through aquifer wall → floods adjacent tiles → creatures in those tiles exposed to fluid → ContaminationSystem picks up material on feet → potential drowning (HealthSystem)
  - Magma breaching → `TileData.FluidType = Magma` on adjacent tiles → ReactionPipeline fires `tile_fluid_is_magma` → items on tile destroyed

---

### CombatSystem
- **Reads**: hostile creature positions vs dwarf positions
- **Writes**: `HealthComponent` (TakeDamage), adds Wound
- **Emits**: `CombatHitEvent`, `CombatMissEvent`
- **Emergent chains**:
  - Wound → bleeding → HealthSystem kills dwarf → ThoughtSystem grief for all survivors → MoodSystem cascade
  - Low Agility dwarf (drunk, MoodSystem debuff) → CombatSystem hit chance drops → misses more attacks → creature lives longer → more damage sustained

---

### ItemSystem
- **Reads**: Commands, world positions
- **Writes**: `Item` entities in EntityRegistry
- **Emits**: `ItemCreatedEvent`, `ItemDestroyedEvent`, `ItemMovedEvent`
- **Emergent chains**:
  - Mining completes → boulder spawned → StockpileManager haul job created → HaulItemStrategy moves boulder → reaction recipe uses it
  - TantrumBehavior destroys item → StockpileManager notes missing item → haul jobs no longer needed

---

### StockpileManager
- **Reads**: item positions, stockpile zone definitions
- **Writes**: haul job creation via JobSystem
- **Emits**: `StockpileCreatedEvent`, `ItemStoredEvent`
- **Emergent chains**:
  - Food stockpile fills up → NeedsSystem jobs complete easily → dwarves stay fed → mood stays stable
  - Stockpile destroyed (tantrum) → food inaccessible → NeedsSystem critical → cascade

---

## Key Emergent Interaction Chains

### 1. The Drunk Cat (classical DF bug)
```
FluidSimulator drops beer to 0 on tile
  → ContaminationSystem: TileData.CoatingMaterialId = "beer"
  → Cat wanders onto tile (BehaviorSystem.WanderBehavior)
  → ContaminationSystem: BodyPartComponent["paws"].Coating = "beer"
  → BehaviorSystem.GroomingBehavior fires
  → ContaminationSystem.IngestCoating → BodyChemistry["alcohol"] += 0.3
  → ReactionPipeline: trigger "entity_has_substance(alcohol, >0.2)" fires
  → EffectApplicator: add_modifier(agility, -0.2) + add_thought(tipsy, +0.15)
  → MoodSystem: cat is marginally happier but more likely to miss attacks
```

### 2. The Tantrum Spiral
```
NeedsSystem: hunger + thirst both critical simultaneously
  → ThoughtSystem: two –happiness thoughts added
  → MoodSystem: Mood drops to Sufferer
  → BehaviorSystem.TantrumBehavior fires
  → Destroys food barrel (ItemSystem.DestroyItem)
  → StockpileManager: food supply drops
  → Other dwarves cannot satisfy hunger → their mood also drops
  → One dwarf attacks another (CombatSystem.AttackEntity)
  → HealthSystem: dwarf dies → DwarfDiedEvent
  → ThoughtSystem: "thought_comrade_died" to ALL survivors (–0.30)
  → Fort-wide mood collapse
```

### 3. Skill-Driven Virtuous Cycle
```
JobSystem assigns mine job
  → MineTileStrategy completes
  → SkillSystem: +25 XP → level-up → SkillLeveledUpEvent
  → ThoughtSystem: positive thought "mastered a skill" (+0.10)
  → MoodSystem: mood improves
  → JobSystem: faster work time for higher-level miners
  → More ore produced → more crafting possible → more items → needs met → better mood
```

### 4. Magma Breach
```
Miner designates wall adjacent to magma
  → MineTileStrategy completes
  → FluidSimulator: magma flows into adjacent tiles
  → ContaminationSystem: entities in path → BodyChemistry["magma_heat"] += high
  → ReactionPipeline: trigger "entity_has_substance(magma_heat, >0.1)"
  → EffectApplicator: damage(50) + add_wound(legs, Critical)
  → HealthSystem: bleeding + death
  → ThoughtSystem: grief cascade
```

### 5. Social Support Breaking Depression
```
Dwarf A: Social need < 0.4, getting unhappy
  → BehaviorSystem.SocializeBehavior fires (Dwarf A + Dwarf B adjacent)
  → NeedsComponent.Social satisfied for both
  → ThoughtSystem: "thought_socialized" (+0.08) added to both
  → MoodSystem: happiness rises
  → Breaks downward spiral before tantrum threshold reached
```

---

## Adding New Emergent Behaviors (No Code Required)

Because the `ReactionPipeline` evaluates data-defined rules, any new interaction
that fits the trigger/effect model needs **only a JSON entry** in `data/reactions/core.json`.

Examples you can add right now:
- Dwarf drinks alcohol item → satisfy recreation + add substance → gets tipsy via reaction
- Dwarf sleeps on bare stone → add thought "slept on stone" (–0.05)
- Entity has high Focus stat → add modifier to skill XP gain rate
- Entity health < 25% → add thought "near death" + add modifier(speed, +0.3) (adrenaline)
