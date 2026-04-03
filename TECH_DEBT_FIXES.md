# Technical Debt Fixes - March 2026

## Summary of Changes

This document describes all technical debt fixes applied to improve game scalability.

---

## 1. CombatSystem: O(n²) → O(n) with Spatial Index

**File:** `src/DwarfFortress.GameLogic/Systems/CombatSystem.cs`

**Problem:** Every tick, iterated ALL hostile creatures × ALL dwarves to find adjacent targets. With 100 creatures and 50 dwarves = 5,000 checks per tick.

**Fix:** Now uses `SpatialIndexSystem` for O(1) neighbor lookups. Falls back to brute force only if spatial index is unavailable.

**Impact:** Combat performance now scales linearly with creature count instead of quadratically.

---

## 2. NeedsSystem: O(n) Job Tracking → O(1)

**File:** `src/DwarfFortress.GameLogic/Systems/NeedsSystem.cs`

**Problem:** `_activeJobIds` cleanup used a linear scan to find matching entries when jobs completed/failed.

**Fix:** Added reverse mapping `_jobIdToKey` dictionary for O(1) removal:
- `_activeJobIds[(entityId, jobDefId)] = jobId` — existence check
- `_jobIdToKey[jobId] = (entityId, jobDefId)` — reverse lookup for removal

**Impact:** Job tracking is now O(1) regardless of active job count.

---

## 3. NutritionSystem/NeedsSystem: Ordering Conflict Fixed

**Files:** 
- `src/DwarfFortress.GameLogic/Systems/NeedsSystem.cs` (Order 5 → 4)
- `src/DwarfFortress.GameLogic/Systems/NutritionSystem.cs` (Order 5 → 6)

**Problem:** Both systems had `UpdateOrder = 5`, causing non-deterministic execution order between them.

**Fix:** 
- NeedsSystem: Order 4 (runs first — decays needs)
- NutritionSystem: Order 6 (runs after — checks deficiencies)

**Impact:** Deterministic system execution order.

---

## 4. NutritionSystem: Data-Driven Nutrition

**Files:**
- `src/DwarfFortress.GameLogic/Data/Defs/ItemDef.cs` — Added `NutritionProfile` record
- `src/DwarfFortress.GameLogic/Data/DataManager.cs` — Added `ParseNutrition()` parser
- `src/DwarfFortress.GameLogic/Systems/NutritionSystem.cs` — Uses explicit nutrition when available
- `data/ConfigBundle/items.json` — Added nutrition data to food items

**Problem:** `ResolveNutritionProfile()` used hardcoded `if (tags.Contains("fruit"))` chains instead of data-driven values.

**Fix:** 
- Added `NutritionProfile` record to `ItemDef` with carbs, protein, fat, vitamins fields
- Updated JSON parser to read `nutrition` block from item definitions
- Updated food items in `items.json` with explicit nutrition values
- Falls back to tag-based inference for backwards compatibility

**Impact:** Adding new food types now only requires JSON changes, no code modifications.

---

## 5. WorldMap: Dirty Tile Save Optimization

**File:** `src/DwarfFortress.GameLogic/World/WorldMap.cs`

**Problem:** `OnSave()` iterated every tile in the entire map (48×48×8 = 18,432 tiles minimum) even though most are empty/default.

**Fix:**
- Added `_dirtyTiles` HashSet to track modified tiles
- `SetTile()` automatically marks tiles as dirty
- `OnSave()` only serializes dirty tiles (falls back to full scan on first save)
- Added `MarkTileDirty()` method for bulk operations
- Dirty tiles cleared after save

**Impact:** Save times now scale with actual changes rather than map size. Subsequent saves are dramatically faster.

---

## 6. JobSystem: Memory Leak Prevention

**File:** `src/DwarfFortress.GameLogic/Jobs/JobSystem.cs`

**Problem:** Job cleanup was duplicated across `FailJob()`, `CompleteJob()`, and `CancelJob()` with slight variations. `_activeWorkAnimations` was not cleaned up in all paths.

**Fix:** Added centralized `CleanupJob(jobId)` method that removes all per-job data from all tracking dictionaries:
- `_stepQueues`
- `_pathQueues`
- `_moveProgress`
- `_activeWorkAnimations`
- `_jobs`

**Impact:** Prevents memory leaks from orphaned job data in tracking dictionaries.

---

## 7. Pathfinder: Zero-Allocation A* Search

**File:** `src/DwarfFortress.GameLogic/World/Pathfinder.cs`

**Problem:** Every pathfinding call created new `Node` objects, `SortedSet`, `HashSet`, and `Dictionary`. With many dwarves pathfinding each tick, this generated massive GC pressure causing frame stutter.

**Fix:**
- Changed `Node` from class to struct (zero heap allocation per node)
- Replaced `SortedSet<Node>` with `List<int>` + linear scan (faster for small-medium open sets)
- Pre-allocated node pool array instead of individual allocations
- Simplified path reconstruction with index-based parent tracking

**Impact:** Pathfinder now generates zero GC allocations per call. Eliminates frame stutter from pathfinding GC pressure.

---

## 8. FluidSimulator: Increased Throughput

**File:** `src/DwarfFortress.GameLogic/Systems/FluidSimulator.cs`

**Problem:** `MaxUpdatesPerTick = 256` limited fluid spread rate, causing slow fluid simulation in larger bodies of water.

**Fix:**
- Increased `MaxUpdatesPerTick` to 512 for better fluid spread
- Added `MinPressureDiff` constant for future pressure-based flow improvements
- Updated documentation to reflect pressure model intent

**Impact:** Fluid simulation now processes twice as many tiles per tick, improving realism for large water bodies.

---

## Files Modified

| File | Change Type | Description |
|------|-------------|-------------|
| `CombatSystem.cs` | Refactor | Use SpatialIndexSystem for neighbor queries |
| `NeedsSystem.cs` | Optimize | O(1) job tracking with reverse mapping |
| `NutritionSystem.cs` | Refactor | Data-driven nutrition with tag fallback |
| `ItemDef.cs` | Feature | Added NutritionProfile record |
| `DataManager.cs` | Feature | Parse nutrition from JSON |
| `items.json` | Data | Added nutrition values to food items |
| `WorldMap.cs` | Optimize | Dirty tile tracking for efficient saves |
| `JobSystem.cs` | Fix | Centralized job cleanup to prevent leaks |
| `Pathfinder.cs` | Optimize | Zero-allocation struct-based A* search |
| `FluidSimulator.cs` | Optimize | Increased throughput, pressure model docs |

---

## 9. JobSystem: Zero-LINQ Hot Path

**File:** `src/DwarfFortress.GameLogic/Jobs/JobSystem.cs`

**Problem:** `TickActiveJobs()` used `.Where(j => j.Status == ...).ToList()` every tick, creating List allocations and iterator objects. `IsDwarfWorking()` and `HasActiveJob()` iterated all jobs per dwarf per assignment cycle (O(n) per call). Command handlers used `.Where().Select().ToHashSet()` chains. All combined to ~60-100 bytes GC per tick scaling with job/dwarf count.

**Fix:**
- Added `_activeJobIds: List<int>` — tracks only InProgress job IDs, maintained on status change via `SetJobStatus()` helper
- Added `_dwarfActiveJobs: Dictionary<int,int>` — O(1) lookup: `dwarfId → jobId` for active jobs
- Replaced `.Where().ToList()` in `TickActiveJobs` with list copy of tracked IDs
- Replaced `IsDwarfWorking` with O(1) dictionary lookup
- Replaced `.Skip(1)` on path arrays with manual loop
- Replaced `.ToHashSet()` in command handlers with manual collection
- `GetPendingJobs` uses iterator block (`yield return`) instead of `.Where()`

**Impact:** Zero GC allocations per tick from LINQ in the job system. O(1) job tracking regardless of active job count.

---

## 10. FluidSimulator: Active Tile Pruning

**File:** `src/DwarfFortress.GameLogic/Systems/FluidSimulator.cs`

**Problem:** `_activeTiles` HashSet grew unbounded — fluid tiles were added when fluid arrived but never removed when fluid dried out. Over long play sessions, the active set retained every tile that had ever touched fluid, even if it was now completely dry.

**Fix:**
- Added `_dryTicks: Dictionary<Vec3i, int>` — tracks consecutive ticks with no fluid change per tile
- `SimulateFluid()` returns bool indicating if flow occurred
- `Tick()` increments dry counter for tiles with no flow; prunes tile after `DryPruneThreshold` (8) consecutive dry ticks
- Resets dry counter on transfer (fluid arrived at new tile)
- Removed unused `stillActive` list from Tick()

**Impact:** Active tile set now shrinks as water dries, preventing unbounded memory growth. Large stagnant water bodies stop being simulated after a few ticks, saving CPU.

---

## Remaining Items (Not Yet Addressed)

These items require more extensive refactoring and should be tackled in future sprints:

1. **EmbarkGenerator Refactoring** — 3,000+ line god class should be split into composable generation passes. This is a large refactoring effort.

2. **WorkshopPanel Snapshot Pattern** — UI directly accesses simulation internals instead of using snapshots. Requires creating WorkshopSnapshot records.

3. **Magic Numbers Extraction** — Hundreds of magic numbers throughout EmbarkGenerator should be moved to configuration files.

4. **GameRoot Refactoring** — 1,200+ line God class should be split into `WorldRenderer`, `CameraController`, `SimulationRunner`, etc.

5. **Data-Driven Water Effects** — Five nearly identical `WaterEffectStyle` records in GameRoot should be loaded from JSON config.

6. **Event Bus Priority System** — EventBus emits to subscribers in registration order. No guarantee of ordering between systems subscribing to the same event. Should add priority parameter to `On<T>()` for deterministic execution.

7. **Chunk-Based WorldMap** — Flat array tile storage (48×48×8 = 18,432 tiles minimum). No chunking for spatial queries. Should implement 16×16×4 chunks with per-chunk dirty/active region tracking.

8. **Full State Save/Load** — Most systems have no-op `OnSave`/`OnLoad`. Only `JobSystem` has partial save support. Fluid state, combat state, world events, and inventory not persisted.

After applying these changes, verify:

1. **Combat** — Hostile creatures still attack adjacent dwarves correctly
2. **Needs** — Dwarves still create eat/drink/sleep jobs when needs are critical
3. **Nutrition** — Food items provide correct nutrition values
4. **Save/Load** — Game saves and loads correctly, with faster subsequent saves
5. **Jobs** — Jobs complete, fail, and cancel without memory leaks
6. **System Order** — Needs decay before nutrition checks run
7. **Jobs (Zero-LINQ)** — Dwarves still pick up and complete jobs correctly; no orphaned job IDs
8. **Fluid (Pruning)** — Fluid still flows correctly; drying tiles stop being simulated after N ticks; active set shrinks over time
