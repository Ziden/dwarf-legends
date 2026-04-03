# Content Pipeline Todo

This tracks the whole-game migration toward a shared, runtime-discovered content architecture.

## Phase 1
- [x] Shared runtime content discovery
- [x] Root ordering and shadow reporting
- [x] Shared validation and query service
- [x] Legacy adapters for runtime and worldgen

## Phase 2
- [x] Plants on the shared runtime/worldgen path
- [x] Tree species on the shared path
- [x] Plant harvesting and seed/fruit hooks through shared queries

## Phase 3
- [x] Material-driven geology veins
- [x] Shared derived forms for boulders, ores, and bars
- [x] Recipe-driven derived outputs for bars and planks
- [x] Wood log/plank forms through shared content queries
- [x] Shipped tree species use distinct wood materials
- [x] Building placement preserves consumed construction materials through footprint, save/load, and query paths
- [x] Client fallback item art resolves convention-based derived forms with material-aware rendering
- [x] More downstream produced forms use shared selectors instead of flat item IDs
- [x] Reduce remaining direct item-ID assumptions around generic resource production

## Phase 4
- [x] Phase 4.1: Creature definitions load from the shared content graph with legacy fallback
- [x] Phase 4.2a: Creature ecology can declare worldgen surface/cave participation through shared content
- [x] Phase 4.2b: Creature-authored history names and profession tendencies feed world history generation
- [x] Phase 4.2c: Creature society roles drive civilization and lore primary-unit selection
- [x] Phase 4.3a: Creature-authored death drops feed runtime item generation
- [x] Phase 4.3b: Reactions and world events can target creature faction roles through runtime defs
- [x] Phase 4.3c: Creature-authored diet drives autonomous feeding without relying on tag inference alone
- [x] Phase 4.3d: Creature-authored movement mode drives traversal without relying on aquatic or fish-tag inference
- [x] Phase 4.3e: Creature-authored hostility drives runtime aggression without relying on the hostile tag alone
- [x] Phase 4.3f: Creature-authored grooming drives contamination ingestion without relying on the groomer tag alone
- [ ] Creatures move fully onto the shared content graph
- [ ] Creature ecology, society, runtime hooks, and history participation unify
- [ ] Add stronger cross-family creature validation

## Phase 5
- [ ] Tile kinds become semantic content
- [ ] Rendering, traversal, opacity, and build/dig roles read tile metadata
- [ ] Remove duplicated tile-kind logic from separate systems

## Phase 6
- [ ] Buildings use shared selectors and roles where possible
- [ ] Recipes and item-template style outputs unify further
- [ ] Reactions and world events move off stringly one-off logic onto stronger content contracts

## Phase 7
- [ ] Civilization archetypes move into shared content
- [ ] Site, trade, profession, and naming participation become content-driven
- [ ] History simulation consumes the shared graph instead of hardcoded tables

## Phase 8
- [ ] AI-first CLI scaffolding and validation workflow
- [ ] ContentEditor understands shared families and compiled reports
- [ ] Add richer diagnostics, previews, and migration tools

## Current Focus
- Phase 3 is complete.
- Phase 4.1 is complete: creature definitions now load through the shared content graph with legacy fallback and shipped bundle files.
- Phase 4.2 is complete for the current slice: creature ecology, history identity, and civilization primary-unit roles now feed worldgen and lore from shared creature content.
- Phase 4.3 is in progress: creature-authored death drops, runtime faction-role selectors, authored diet, authored movement mode, authored hostility, and authored grooming now participate in gameplay.
- Next up is the remaining Phase 4 work beyond this checkpoint: a single product or taming seam if we return to Phase 4 later.
