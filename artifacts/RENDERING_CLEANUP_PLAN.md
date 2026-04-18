# Rendering Cleanup Plan

## Summary

- Remove the current entity-highlight system before rebuilding emphasis.
- Keep billboard picking and selection working, but remove billboard-specific hover/designation visuals for actors, items, trees, and plants.
- Document rendering ownership in `Scripts/Rendering/RENDERING.md`.
- Rebuild entity emphasis later as a separate system with explicit ownership.

## Scope

This cleanup pass removes:

- vegetation hover, designation, and area-selection billboard emphasis
- actor and item billboard hover emphasis
- shared billboard outline shader/material plumbing used only for entity emphasis
- dead legacy tree/plant billboard emphasis code still living in `WorldRender3D`

This cleanup pass keeps:

- terrain, liquid, and detail rendering
- chunk streaming and chunk-mesh residency
- actor, item, tree, and plant billboard rendering
- billboard picking and selection
- terrain/tile/world overlays such as tile hover plates, designation overlays, and stockpile overlays

## Sequence

1. Add `Scripts/Rendering/RENDERING.md` documenting the rendering architecture and target ownership boundaries.
2. Remove vegetation emphasis rendering from `VegetationInstanceRenderer`, while keeping shared vegetation rendering and alpha-aware picking.
3. Remove actor/item hover emphasis from `WorldActorPresentation3D`, while keeping billboard rendering and billboard picking.
4. Delete dead tree/plant billboard highlight plumbing from `WorldRender3D`.
5. Remove unused shared entity-outline assets if no remaining renderer depends on them.
6. Update focused smoke tests so they validate selection and picking without depending on highlight visuals.
7. Run focused build and rendering smoke validation.

## Target Ownership After Cleanup

- `WorldRender3D`
  Coordinates terrain, structures, actor presentation, vegetation rendering, and tile/world overlays.
  It should not own vegetation highlight state or dead tree/plant billboard state.

- `WorldActorPresentation3D`
  Owns actor/item billboard rendering and actor/item billboard picking.
  It should not own billboard hover-outline visuals after this cleanup.

- `VegetationInstanceRenderer`
  Owns shared vegetation billboard rendering and vegetation picking.
  It should not own hover/designation/selection visuals after this cleanup.

- Tile/world overlays
  Remain separate from entity billboard emphasis.

## Follow-Up

- Rebuild entity emphasis as a dedicated system after the cleanup baseline is stable.
- Preferred direction:
  shared main billboard rendering
  separate picker
  separate emphasis renderer keyed by tile/entity identity
