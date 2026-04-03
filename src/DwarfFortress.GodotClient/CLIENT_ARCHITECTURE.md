# Client Architecture

This client mirrors the GameLogic goal of clear ownership and low coupling.

## Layers

- `Scripts/App`
  Scene composition roots and top-level flow only.
  Examples: `GameRoot`, `MainMenu`.
- `Scripts/Bootstrap`
  Simulation startup, content lookup, and engine-facing infrastructure adapters.
  Examples: `ClientSimulationFactory`, `ClientContentQueries`, `GodotLogger`.
- `Scripts/Presentation`
  Cross-cutting presentation orchestration that is not itself a scene widget.
  Examples: simulation loop timing, world feedback state.
- `Scripts/Input`
  Input capture, hover state, and command intent mapping.
- `Scripts/UI`
  Canvas/UI nodes and formatting helpers for panels, overlays, and menus.
- `Scripts/Rendering`
  World rendering, sprite/texture generation, meshers, and visual resolvers.
  `Rendering/Terrain` contains tile composition and smoothing helpers.
  `Rendering/Visuals` contains reusable sprite/texture generation infrastructure.
- `Scripts/WorldGen`
  World generation viewer entrypoints and tool-specific scene roots.
- `Scripts/Diagnostics`
  Client smoke tests, profiler bridges, and debug-only support code.

## Naming Rules

- `*Root`
  Scene entrypoint or composition root only.
- `*Panel`, `*Menu`, `*Window`
  UI nodes only.
- `*Controller`
  Stateful orchestration around input or presentation flow.
- `*Resolver`
  Pure or mostly-pure translation/query logic.
- `*Mesher`
  Mesh construction only.
- `*Factory`
  Asset or object creation only.
- `*Registry`
  Cached lookup store keyed by stable IDs.

## Guardrails

- Keep GameLogic integration behind `Bootstrap`, `App`, or explicit query adapters.
- Keep rendering helpers out of `UI` and gameplay bootstrapping out of `Rendering`.
- Prefer adding new files to an existing layer before creating another top-level bucket.
- Match folder and namespace: `DwarfFortress.GodotClient.<Layer>[.<Subarea>]`.
