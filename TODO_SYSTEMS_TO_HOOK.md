# TODO — Systems / Stats Not Yet Hooked

Anything listed here exists in the codebase but has no runtime wiring.
Cross this off as each item is implemented.

---

## WorldLoreSystem — Dynamic Stat Updates

### `WorldLoreState.Prosperity`
- **Current state**: Set once during world generation (`WorldLoreGenerator`). Never mutated during simulation.
- **Read by**: `WorldLoreSystem.ScaleMigrantCount()`, `TuneEventProbability("migrant_wave")`, `WorldLorePanel` display bar.
- **Should respond to (suggested deltas)**:
  | Event | Delta |
  |-------|-------|
  | `RecipeCraftedEvent` | +0.001 per craft |
  | `ItemStoredEvent` | +0.0005 per haul |
  | `JobCompletedEvent` (any productive labor) | +0.0005 |
  | `DwarfDiedEvent` | -0.02 (losing citizens hurts) |
  | `WorldEventFiredEvent("goblin_raid")` | -0.03 (attacks reduce confidence) |
  | `WorldEventFiredEvent("migrant_wave")` | +0.01 (word is spreading) |
- **Implementation hint**: Add `WorldLoreSystem.ApplyProsperityDelta(float delta)` (clamped 0–1), subscribe to events inside `WorldLoreSystem.Initialize()`.
- **Save note**: `WorldLoreState` is already serialized — mutations on the object will be persisted automatically.

### `WorldLoreState.Threat`
- **Current state**: Set once during world generation. Never mutated during simulation.
- **Read by**: `WorldLoreSystem.ScaleRaidCount()`, `TuneEventProbability("goblin_raid")`, `WorldLorePanel` display bar.
- **Should respond to (suggested deltas)**:
  | Event | Delta |
  |-------|-------|
  | `EntityKilledEvent` (hostile entity) | -0.015 per kill |
  | `DwarfDiedEvent` (combat cause) | +0.02 (raids are succeeding) |
  | `WorldEventFiredEvent("goblin_raid")` | +0.025 |
  | Time without raids (sustained peace, ~1 season) | -0.005/tick |
- **Implementation hint**: Same helper approach as Prosperity. Optionally add time-based decay in `WorldLoreSystem.Tick()` (currently a no-op).

### `WorldLoreState.FactionRelations`
- `FactionRelationLoreState.Score` is set at worldgen; never changes.
- Not currently read by any runtime system, but `GetPrimaryHostileUnitDefId` could weight by relation score.
- **Low priority** until diplomacy / trade mechanics are added.

### `WorldLoreState.Sites[*]` — Development & Security
- `SiteLoreState.Development` and `.Security` set at worldgen; never updated.
- Not read by any runtime system yet.
- **Low priority** until regional-map or trade-route mechanics exist.

### `WorldLoreState.Factions[*]` — Influence / Militarism / TradeFocus
- All faction numeric stats are worldgen-only.
- `GetPrimaryHostileUnitDefId` already uses `Militarism * Influence` ordering — if these never change, the threat faction is permanently fixed.
- **Low priority** — revisit when faction-event scripting is added.

---

## Orphaned Events (Fired But Never Subscribed)

### `SkillLeveledUpEvent`
- **Fired by**: `SkillSystem` when a dwarf levels up a skill.
- **Subscribers**: None anywhere in the codebase.
- **Should**: Log in `AnnouncementLog` (e.g., "Aban is now a Skilled Miner!"), optionally grant a happiness thought via `ThoughtSystem`.

### `RecipeCraftedEvent`
- **Fired by**: `RecipeSystem` on successful workshop craft.
- **Subscribers**: None. `GameRoot` only logs `ProductionOrderCreatedEvent` (order queued), not the actual completion.
- **Should**: Log craft completions in `AnnouncementLog`; feed Prosperity delta (see above).

### `EntityKilledEvent`
- **Fired by**: `EntityRegistry` for every entity death (dwarves + creatures).
- **Subscribers**: None. `DwarfDiedEvent` (from `HealthSystem`) is logged, but creature/hostile kills are not.
- **Should**: Feed Threat reduction when the killed entity is hostile; show kill announcement for notable combats.

### `ItemStoredEvent`
- **Fired by**: `StockpileManager` when an item is hauled into a stockpile slot.
- **Subscribers**: None.
- **Should**: Feed Prosperity delta; optionally suppress — depends on how noisy it would be.

### `ReactionFiredEvent`
- **Fired by**: `ReactionPipeline`.
- **Subscribers**: None.
- **Low priority** — purely internal processing, but could be used for reaction-specific announcements.

---

## `WorldLoreSystem.Tick` is Intentionally Empty
- Currently `public void Tick(float delta) { }` — a no-op.
- If time-based Threat decay or slow Prosperity recovery is added, this is where it goes.
- No action needed until dynamic stat updates (above) are implemented.

---

## Summary Checklist

- [ ] `WorldLoreState.Prosperity` — wire runtime deltas via events
- [ ] `WorldLoreState.Threat` — wire runtime deltas via events
- [ ] `SkillLeveledUpEvent` — add announcement + optional thought
- [ ] `RecipeCraftedEvent` — add announcement + Prosperity delta
- [ ] `EntityKilledEvent` — log hostile kills, feed Threat delta
- [ ] `ItemStoredEvent` — feed Prosperity delta (decide granularity)
- [ ] `WorldLoreState.FactionRelations` — dynamic scores (low priority)
- [ ] `WorldLoreState.Sites` — dynamic development/security (low priority)
