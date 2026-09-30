### Fixed

- `V.Particles` draws a world-space or custom-space source at the element, as it draws a local-space one.
  Such a source used to warn on mount and draw its particles far outside the element, read as local
  positions of a host parked away from the origin.

- `V.Particles` draws every live particle of a system. It drew at most 2048 and warned on mount when an
  effect declared more.

- `V.SceneView` on a runtime panel re-derives its texture when the panel's pixel density changes, as it did
  on an editor panel. A panel-scale change moves no geometry, so the texture kept its old size until the
  element's next layout or props change.
