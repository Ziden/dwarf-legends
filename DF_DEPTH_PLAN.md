# DF-Depth Plan: Provenance, History, and Site Embodiment

## 1. Goal

Raise world generation and runtime integration from coherent terrain plus light lore to a DF-grade causal simulation where:

1. Every embark exists inside a persistent world with real civilizations, sites, roads, populations, and conflicts.
2. Every dwarf, migrant, raider, and notable creature can be explained by generated history.
3. Region and local maps preserve historical identity instead of collapsing it into booleans and flavor text.
4. Runtime systems consume one canonical world-history model rather than parallel lore and mapgen tracks.

This plan is ordered by leverage. It intentionally prioritizes representational capacity and runtime carry-through before further terrain polish.

---

## 2. Current State Summary

### Strong today

1. World, region, and local generation are deterministic and staged.
2. Hydrology continuity, ecology coherence, and local diagnostics are in good shape.
3. Geology, ores, trees, wildlife, and plants are increasingly data-driven.
4. A layered history pipeline already exists in `DwarfFortress.WorldGen.History`.

### Main depth blockers

1. History has no figures, households, births, deaths, family links, migrations, or population accounting.
2. The runtime still uses `WorldLoreSystem` while layered mapgen/history uses `MapGenerationService` and `GeneratedWorldHistory`.
3. Region/local generation collapses historical data into `HasSettlement`, `HasRoad`, and influence masks too early.
4. Runtime dwarves and migrants are not linked to civilization, site, household, or world-history records.
5. Terrain analysis is plausible but still misses some macro-to-region causal coupling, especially mountain-to-slope propagation.

---

## 3. Architectural End State

The target data flow is:

```
Seed
  -> WorldLayerGenerator
  -> HistorySimulator
  -> RegionLayerGenerator
  -> LocalLayerGenerator
  -> FortressBootstrap / Runtime Spawn Systems
  -> Query + UI
```

With one canonical history model:

1. `GeneratedWorldHistory` owns civilizations, figures, households, sites, roads, population, migrations, and events.
2. `MapGenerationService` is the authoritative access point for generated world state.
3. `WorldLoreSystem` becomes either:
   - a thin projection over canonical history, or
   - a compatibility shim to be removed after migration.
4. Runtime entities reference history records through explicit provenance components.

---

## 4. Guiding Principles

1. No parallel truths. Runtime systems must not invent a separate world-history reality.
2. Preserve identity across scales. A site, road, ruin, or lineage should survive world -> region -> local -> runtime.
3. Prefer causal simulation over flavor generation. Narrative text should summarize world state, not replace it.
4. Add data models before adding decorations. If the simulation cannot represent a thing, visual polish is wasted.
5. Keep the pipeline deterministic and replayable at every step.

---

## 5. Delivery Order

## Phase 0: Canonical History Unification

### Objective

Make `GeneratedWorldHistory` the only authoritative macro-history source used by generation and runtime.

### Problems solved

1. Removes the split between `WorldLoreSystem` and `MapGenerationService`.
2. Prevents history-driven mapgen from diverging from runtime event spawning.
3. Creates a stable base for provenance work.

### Code areas to change

1. `src/DwarfFortress.GameLogic/World/MapGenerationService.cs`
2. `src/DwarfFortress.GameLogic/Systems/WorldLoreSystem.cs`
3. `src/DwarfFortress.GameLogic/Systems/FortressBootstrapSystem.cs`
4. `src/DwarfFortress.GameLogic/Systems/WorldEventManager.cs`
5. `src/DwarfFortress.GameLogic/Systems/WorldQuerySystem.cs`
6. `src/DwarfFortress.GameLogic/Systems/WorldQueryModels.cs`

### Implementation tasks

1. Introduce a `WorldHistoryRuntimeService` in GameLogic as the read-side adapter for `GeneratedWorldHistory`.
2. Refactor `WorldLoreSystem` so it reads canonical history projections instead of generating standalone lore.
3. Replace fortress-start biome/lore lookup with canonical history and embark context from `MapGenerationService`.
4. Replace migrant and raid scaling to use canonical civilization and threat data.
5. Preserve backward compatibility during transition with a temporary compatibility shim.

### Acceptance criteria

1. Starting a fortress uses history from `MapGenerationService`, not a separate lore generation pass.
2. Runtime queries expose a summary derived from canonical history.
3. No system generates macro history independently after fortress start.

### Tests

1. `MapGenerationServiceTests`: canonical history is available after embark generation.
2. New tests for `WorldHistoryRuntimeService`: deterministic projection and summary generation.
3. `FortressBootstrapSystemTests`: startup uses canonical history path.
4. `WorldEventManagerTests`: event scaling reads canonical history instead of `WorldLoreGenerator` output.

---

## Phase 1: Figure, Household, and Population Substrate

### Objective

Add the minimum world-history data structures needed to explain who exists in the world and why.

### New records to introduce

1. `HistoricalFigureRecord`
2. `HouseholdRecord`
3. `SitePopulationRecord`
4. `MigrationRecord`
5. `FigureEventRecord` or richer figure-linked event metadata

### Proposed fields

#### HistoricalFigureRecord

1. `Id`
2. `Name`
3. `SpeciesDefId`
4. `CivilizationId`
5. `BirthSiteId`
6. `CurrentSiteId`
7. `HouseholdId`
8. `BirthYear`
9. `DeathYear`
10. `ProfessionId`
11. `MotherFigureId`
12. `FatherFigureId`
13. `SpouseFigureIds`
14. `IsRuler`
15. `IsAlive`

#### HouseholdRecord

1. `Id`
2. `CivilizationId`
3. `HomeSiteId`
4. `MemberFigureIds`
5. `WealthBand`
6. `OccupationProfileId`

#### SitePopulationRecord

1. `SiteId`
2. `Year`
3. `Population`
4. `HouseholdCount`
5. `MilitaryCount`
6. `CraftCount`
7. `AgrarianCount`
8. `MiningCount`

#### MigrationRecord

1. `Id`
2. `FigureId`
3. `FromSiteId`
4. `ToSiteId`
5. `Year`
6. `Reason`

### Code areas to change

1. `src/DwarfFortress.WorldGen/History/GeneratedWorldHistory.cs`
2. `src/DwarfFortress.WorldGen/History/HistoryYearSnapshot.cs`
3. `src/DwarfFortress.WorldGen/History/HistorySimulator.cs`
4. `src/DwarfFortress.WorldGen/History/GeneratedWorldHistoryTimeline.cs`

### Simulation rules to add

1. Seed initial founders for each civilization around capital sites.
2. Group founders into households and assign professions.
3. Track births and deaths yearly.
4. Let site population grow or shrink based on prosperity, threat, food support, and connectivity.
5. Emit migrations when sites decline, grow beyond carrying capacity, or establish daughter settlements.
6. Mark rulers, military leaders, and notable founders for later UI surfacing.

### Acceptance criteria

1. A generated world contains living figures and households for each civilization.
2. Each site has explicit population records.
3. Annual simulation can explain site growth and decline in terms of figure and household movement.
4. A sample figure can be traced to parents, birthplace, civilization, and household.

### Tests

1. `HistorySimulatorTests`: founders are created for every civilization.
2. `HistorySimulatorTests`: at least one household exists per settled civ.
3. `HistorySimulatorTests`: yearly simulation mutates population consistently.
4. `HistorySimulatorTests`: migration records link valid figures and sites.
5. `HistorySimulatorTests`: genealogical links never point to missing figures.

---

## Phase 2: Site Identity and Localized History Embodiment

### Objective

Stop collapsing sites and roads into booleans. Preserve what kind of place exists in the world and how it should manifest in a region or embark.

### New concepts

1. `RegionSiteOverlay`
2. `LocalSiteDescriptor`
3. `HistoricalScarDescriptor`
4. `LocalRoadDescriptor`

### Site descriptor content

1. Site id
2. Site kind
3. Owner civilization id
4. Status: active, declining, abandoned, ruined, occupied
5. Development band
6. Security band
7. Local footprint archetype
8. Historical tags: battlefield, shrine, watchpost, cemetery, trade outpost, mine, ruined hamlet

### Code areas to change

1. `src/DwarfFortress.WorldGen/Regions/RegionLayerGenerator.cs`
2. `src/DwarfFortress.WorldGen/Local/LocalLayerGenerator.cs`
3. `src/DwarfFortress.WorldGen/Maps/EmbarkGenerator.cs`
4. `src/DwarfFortress.WorldGen/Maps/GeneratedEmbarkMap.cs`

### Implementation tasks

1. Replace the history overlay mask approach with explicit region-local site overlays.
2. Preserve road identity through region and local generation instead of projecting to anonymous road tiles.
3. Localize specific site types into map features:
   - fortress core
   - ruined walls
   - road segments
   - watchtower remains
   - grave fields
   - mine entrances
   - shrines
   - abandoned farms
4. Add historical scars from events:
   - burned ground
   - rubble fields
   - disturbed graves
   - collapsed tunnels
   - siege roads or fortified approaches

### Acceptance criteria

1. A region with historical sites can name and place those sites explicitly.
2. A local embark can identify intersecting sites and roads by id and type.
3. Ruins and settlements differ materially in output, not just through `HasSettlement`.

### Tests

1. `RegionLayerGeneratorTests`: history overlays preserve site ids and road endpoints.
2. `LocalLayerGeneratorTests`: localized site descriptors are produced deterministically.
3. `EmbarkGeneratorTests`: local maps with ruins and roads differ structurally from empty wilderness.

---

## Phase 3: Runtime Provenance Components and History-Derived Entrants

### Objective

Make playable dwarves, migrants, raiders, and notable creatures derive from generated history.

### New runtime component

`DwarfProvenanceComponent`

### Proposed fields

1. `FigureId`
2. `CivilizationId`
3. `BirthSiteId`
4. `HomeSiteId`
5. `HouseholdId`
6. `MigrationWaveId`
7. `ArrivalReason`
8. `GenerationRole` such as founder, migrant, exile, soldier, caravan guard

### Code areas to change

1. `src/DwarfFortress.GameLogic/Entities/Dwarf.cs`
2. `src/DwarfFortress.GameLogic/Entities/EntityRegistry.cs`
3. `src/DwarfFortress.GameLogic/Systems/FortressBootstrapSystem.cs`
4. `src/DwarfFortress.GameLogic/Systems/WorldEventManager.cs`
5. `src/DwarfFortress.GameLogic/Systems/WorldQueryModels.cs`
6. `src/DwarfFortress.GameLogic/Systems/WorldQuerySystem.cs`

### Implementation tasks

1. Introduce provenance components for dwarves and optionally creatures.
2. Replace hardcoded embark dwarves with a selected founding party from canonical history.
3. Replace generic migrant spawns with a migration-wave selection from source sites.
4. Replace generic hostile spawns with history-linked military or raiding parties.
5. Expose provenance through query models and UI summaries.

### Acceptance criteria

1. Starting dwarves each map to a canonical historical figure record.
2. Migrants arrive from real source sites and civilizations.
3. Raiders have real faction and site origin.
4. Querying a dwarf can show birthplace, home site, civ, and arrival reason.

### Tests

1. `FortressBootstrapSystemTests`: generated founding party comes from canonical history.
2. `WorldEventManagerTests`: migrants and hostiles resolve valid source civs and sites.
3. `WorldQuerySystemTests`: dwarf provenance is visible through query models.

---

## Phase 4: Economic, Military, and Cultural Deepening

### Objective

Make site growth and conflict derive from more than threat and prosperity scalars.

### New simulation layers

1. Site specialization
2. Trade corridor value
3. Resource extraction capacity
4. Military campaign state
5. Cultural identity propagation

### Required additions

1. Site production profiles tied to terrain and geology.
2. Trade importance derived from road connectivity, river access, and neighboring sites.
3. Campaign records for wars, sieges, occupations, and reconquest.
4. Cultural tags or identity groups attached to civilizations and households.

### Code areas to change

1. `src/DwarfFortress.WorldGen/History/HistorySimulator.cs`
2. `src/DwarfFortress.WorldGen/History/CivilizationRecord.cs`
3. `src/DwarfFortress.WorldGen/History/SiteRecord.cs`
4. `src/DwarfFortress.WorldGen/History/RoadRecord.cs`
5. region/local site embodiment code from Phase 2

### Acceptance criteria

1. Sites have differentiated roles such as mine, shrine, trade hub, fortress, hamlet.
2. Road networks reflect actual economic and political relationships.
3. Wars produce lasting site and territory consequences.
4. Cultural identity affects names, migrants, and potentially preferences later.

### Tests

1. `HistorySimulatorTests`: site roles correlate with terrain and connectivity.
2. `HistorySimulatorTests`: wars can change ownership and leave occupied or ruined sites.
3. `HistorySimulatorTests`: roads preferentially connect economically relevant sites.

---

## Phase 5: Terrain and Geology Realism Follow-Through

### Objective

Fix macro-to-region and region-to-local terrain causality gaps once historical depth has somewhere to land.

### Current known issue

Pipeline analysis shows weak or failing mountain-to-region slope response. This means world relief signals are not propagating strongly enough into region-scale slope and local play spaces.

### Code areas to revisit

1. `src/DwarfFortress.WorldGen/World/WorldLayerGenerator.cs`
2. `src/DwarfFortress.WorldGen/Regions/RegionLayerGenerator.cs`
3. `src/DwarfFortress.WorldGen/Analysis/WorldGenAnalyzer.cs`
4. `MAPGEN_REALISM_PLAN.md`

### Implementation tasks

1. Strengthen mountain, relief, and basin propagation from world to region.
2. Broaden biome share diversity and climatic contrasts.
3. Tie settlement viability and site specialization more tightly to terrain realism.
4. Add history-aware terrain scars where relevant.

### Acceptance criteria

1. World mountain cover positively correlates with region slope and rocky local output.
2. Biome distribution has stronger extremes and more geographic contrast.
3. Terrain realism increases strategic variation between sites and embarks.

### Tests

1. Extend `WorldGenAnalyzerTests` with stronger terrain causality budgets.
2. Add seed-sweep property tests for mountain, river, and site correlation.

---

## 6. Transitional Strategy

Use staged migration rather than a single cutover.

### Stage A

1. Canonical history exists and runtime can read it.
2. `WorldLoreSystem` still exists as adapter compatibility.

### Stage B

1. Runtime migrants and founding dwarves use canonical history.
2. Query/UI starts exposing provenance.

### Stage C

1. Region/local history overlay preserves typed site identity.
2. Old boolean-only settlement model becomes legacy fallback.

### Stage D

1. Remove standalone lore generation as authoritative source.
2. Retain only projection helpers for UI summary generation.

---

## 7. File-Level Worklist

### New files likely required

1. `src/DwarfFortress.WorldGen/History/HistoricalFigureRecord.cs`
2. `src/DwarfFortress.WorldGen/History/HouseholdRecord.cs`
3. `src/DwarfFortress.WorldGen/History/SitePopulationRecord.cs`
4. `src/DwarfFortress.WorldGen/History/MigrationRecord.cs`
5. `src/DwarfFortress.GameLogic/Entities/Components/DwarfProvenanceComponent.cs`
6. `src/DwarfFortress.GameLogic/World/WorldHistoryRuntimeService.cs`
7. `src/DwarfFortress.WorldGen/Regions/RegionSiteOverlay.cs`
8. `src/DwarfFortress.WorldGen/Local/LocalSiteDescriptor.cs`

### Existing files with major changes

1. `src/DwarfFortress.WorldGen/History/GeneratedWorldHistory.cs`
2. `src/DwarfFortress.WorldGen/History/HistoryYearSnapshot.cs`
3. `src/DwarfFortress.WorldGen/History/HistorySimulator.cs`
4. `src/DwarfFortress.WorldGen/Regions/RegionLayerGenerator.cs`
5. `src/DwarfFortress.WorldGen/Local/LocalLayerGenerator.cs`
6. `src/DwarfFortress.WorldGen/Maps/EmbarkGenerator.cs`
7. `src/DwarfFortress.GameLogic/Entities/Dwarf.cs`
8. `src/DwarfFortress.GameLogic/Systems/FortressBootstrapSystem.cs`
9. `src/DwarfFortress.GameLogic/Systems/WorldEventManager.cs`
10. `src/DwarfFortress.GameLogic/Systems/WorldQueryModels.cs`
11. `src/DwarfFortress.GameLogic/Systems/WorldQuerySystem.cs`
12. `src/DwarfFortress.GameLogic/Systems/WorldLoreSystem.cs`

---

## 8. Test Plan

### History depth tests

1. Every civilization has at least one founder household.
2. Every living figure belongs to a valid civilization and site.
3. Every migration event references valid source and destination sites.
4. Figures cannot be born after they die.
5. Parent links are acyclic and valid.

### Region/local embodiment tests

1. Historical sites survive world -> region -> local projection with stable ids.
2. Local site descriptors are deterministic for a seed.
3. Roads preserve intended endpoints and identity.
4. Ruined and active site outputs are structurally different.

### Runtime provenance tests

1. Founding dwarves carry provenance metadata.
2. Migrants and raiders map back to canonical history.
3. Query models expose provenance correctly.

### Analyzer expansion

Add budgets for:

1. figures per civilization
2. households per inhabited site
3. migration density
4. active vs ruined site mix
5. runtime provenance coverage
6. site-localization coverage

---

## 9. Rollout Milestones

## Milestone D1: Canonical history cutover

1. Runtime systems read `GeneratedWorldHistory`.
2. Compatibility shim remains.

## Milestone D2: Figure substrate

1. Figures, households, and population records exist.
2. Timeline snapshots include them.

## Milestone D3: Provenance-enabled fortress start

1. Starting dwarves come from canonical history.
2. Migrants and raids come from canonical history.

## Milestone D4: Site embodiment

1. Sites and roads localize into typed map features.
2. Query/UI can name intersecting sites.

## Milestone D5: Economy and war depth

1. Site roles, trade, campaigns, and ruin formation are simulated.

## Milestone D6: Terrain follow-through

1. Macro terrain realism budgets are tightened after provenance pipeline is stable.

---

## 10. Risks and Controls

### Risk: complexity spike in history simulation

Control:
1. Add representational layers incrementally.
2. Keep yearly simulation simple and testable.
3. Use deterministic summaries and seed-sweep tests.

### Risk: runtime and history drift during migration

Control:
1. Keep one canonical source.
2. Use adapter shims only temporarily.
3. Add tests that compare runtime spawns to source history.

### Risk: local map clutter from historical embodiment

Control:
1. Use typed descriptors and density budgets.
2. Limit historical imprint by site size, recency, and importance.

### Risk: performance regression

Control:
1. Profile history generation separately from map generation.
2. Cache generated history in `MapGenerationService`.
3. Keep figure counts scalable by world size and civ strength.

---

## 11. Recommended Immediate Next Slice

The highest-leverage first implementation slice is:

1. Create figure, household, and migration record types.
2. Extend `GeneratedWorldHistory` and `HistoryYearSnapshot` to carry them.
3. Update `HistorySimulator` to seed founder households and living figures.
4. Add a GameLogic `WorldHistoryRuntimeService` adapter.
5. Add `DwarfProvenanceComponent` and wire fortress founders from canonical history.

This is the shortest path to visible DF-style depth, because it gives the game real people before adding more decorative world detail.
