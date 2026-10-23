### Added

- `V.Particles` takes a VFX Graph `VisualEffect`: the element clones it into a hidden host, renders the clone with a
  camera of its own into a texture the element's size on its tick, and draws that texture over its box. The clone
  stands on a layer the project has not named, where one is left, and its renderers are forced off for every other
  camera.
