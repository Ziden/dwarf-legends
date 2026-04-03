# Civilization Simulation Plan

## Goal

Move from "macro lore plus seeded factions" to a world where civilizations, sites, households, figures, migration, pressure, and conflict have enough structure to explain runtime events.

This should deepen migrants, raids, diplomacy, provenance, and site identity without turning the game into an unreadable simulation wall.

## Current state

The codebase is further along than some older docs suggest.

Already implemented:

- `GeneratedWorldHistory` includes civilizations, sites, households, figures, roads, events, and territory ownership.
- `HistorySimulator` already emits household and figure records.
- `WorldHistoryRuntimeService` projects generated history into runtime-friendly snapshots.
- starting dwarves can already receive provenance derived from runtime history.

Still missing or still shallow:

- no explicit per-site population records over time
- no migration records as first-class history outputs
- no family links, births, and deaths with genealogical continuity
- no army, caravan, or military organization model
- no true diplomacy state transitions beyond simple faction metrics
- raids and migrants are not yet selected from concrete site population pools
- runtime world pressure still leans on lore scalars instead of fully causally derived civ state

## Design principles

1. One canonical history truth.
2. Runtime events should come from historical state, not flavor-only generators.
3. Simulate only what drives gameplay and explanation.
4. Prefer coarse but causal systems over huge decorative record sets.
5. Preserve determinism across world generation and runtime projections.

## Canonical model

The civilization simulation should produce and maintain:

- civilizations
- sites
- households
- figures
- site populations
- migration records
- road/trade connectivity
- military pressure and frontier pressure
- diplomacy / hostility state
- historical events tied to real actors and locations

## Recommended new records

### `SitePopulationRecord`

Purpose:

- track how a site grows, declines, starves, militarizes, or empties over time

Fields:

- `SiteId`
- `Year`
- `Population`
- `HouseholdCount`
- `MilitaryCount`
- `CraftCount`
- `AgrarianCount`
- `MiningCount`
- `Prosperity`
- `Security`

### `MigrationRecord`

Purpose:

- explain migrant waves, resettlement, and refugee movement

Fields:

- `Id`
- `Year`
- `FromSiteId`
- `ToSiteId`
- `CivilizationId`
- `FigureIds`
- `Reason`

### `DiplomaticStateRecord`

Purpose:

- track more than a static hostile flag

Fields:

- `CivilizationAId`
- `CivilizationBId`
- `RelationScore`
- `WarState`
- `TradeState`
- `LastConflictYear`

## Delivery phases

## Phase 1: Population substrate

Objective:

- make site population explicit instead of inferred from site flavor

Changes:

- add `SitePopulationRecord`
- teach `HistorySimulator` to project yearly population state per site
- tie site development/security more directly to population composition

Gameplay payoff:

- better migrant scaling
- better raid target logic
- clearer site importance

## Phase 2: Migration and settlement pressure

Objective:

- let people actually move between sites for reasons

Changes:

- add `MigrationRecord`
- simulate out-migration from overcrowded, starving, or threatened sites
- simulate daughter-settlement founding from prosperous sites

Gameplay payoff:

- migrant waves can name where they came from and why
- raids and caravans can have meaningful origin sites

## Phase 3: Figure identity expansion

Objective:

- make figures feel historically grounded, not just named units

Changes:

- add kinship links, births, and deaths
- track spouses, parents, and children where useful
- make profession and skill history evolve over time

Gameplay payoff:

- better dwarf provenance
- notable migrants and raiders
- future memorial / revenge / lineage stories

## Phase 4: Diplomacy and military pressure

Objective:

- replace simple scalar threat with civ relationships and frontier pressure

Changes:

- add diplomacy state between civilizations
- model military pressure from nearby hostile sites
- weight raids by distance, road access, prosperity, and hostility

Gameplay payoff:

- raids feel geographically and politically grounded
- prosperity and threat respond to real world actors

## Phase 5: Runtime consumers

Objective:

- make GameLogic systems consume this history directly

Changes:

- migrant generation draws from population/migration pools
- raid generation draws from hostile frontier sites and military pressure
- caravans draw from trade-connected sites
- `WorldQuerySystem` exposes civ/site summaries for UI

Gameplay payoff:

- world events stop feeling generic
- the world becomes explainable from UI and logs

## Phase 6: Retired-fort integration

Objective:

- let forts modify the same world history

Changes:

- record fortress founding, decline, retire, reclaim, destruction into canonical history
- allow new forts and later migrants to reference old forts and their reputations

Gameplay payoff:

- true DF-style shared world continuity

## Runtime integration targets

These systems should become consumers of richer civ state:

- `WorldHistoryRuntimeService`
- `WorldLoreSystem`
- `WorldEventManager`
- `FortressBootstrapSystem`
- `WorldQuerySystem`
- future trade / diplomacy / reclaim flows

## Implementation notes for current codebase

### Do not throw away what already exists

The correct move is extension, not restart:

- keep `GeneratedWorldHistory`
- extend `HistorySimulator`
- keep `WorldHistoryRuntimeService` as the runtime projection layer
- gradually reduce `WorldLoreSystem` from pseudo-source-of-truth toward a tuning/projection layer

### Use provenance as the first proving ground

If a history feature cannot explain one dwarf, one migrant wave, and one raid, it is not integrated enough.

## Tests to require

1. Deterministic history generation for same seed and settings.
2. Every site has a valid population record by the end of simulation.
3. Every migration references valid sites and figures.
4. Runtime migrant selection pulls valid figures or households from canonical history.
5. Raid source site selection follows hostility plus frontier pressure rules.

## Recommended execution order

1. Site population records.
2. Migration records.
3. Figure kinship and lifecycle.
4. Diplomacy and military pressure.
5. Runtime consumer rewiring.
6. Retired-fort world integration.

## Definition of done

Civilization simulation is in the right place when:

- migrants can be explained by origin site, household, and pressure reason
- raids can be explained by a real hostile site and political state
- site importance changes over time for visible reasons
- world query UI can summarize that state without inventing parallel lore