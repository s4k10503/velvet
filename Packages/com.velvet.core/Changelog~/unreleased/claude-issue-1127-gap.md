### Fixed

- `V.Particles` draws a sub-emitter that is not a child of the effect. Such a sub-emitter used to stay a
  reference to the source's own system, so its particles were not among those drawn; it is now cloned under
  the hidden host and the reference re-pointed.
