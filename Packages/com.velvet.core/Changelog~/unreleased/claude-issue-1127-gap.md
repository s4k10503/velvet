### Fixed

- `V.Particles` draws a sub-emitter that is not a child of the effect. The sub-emitter used to stay a
  reference to the source's own system, so the hidden host triggered a system nobody simulated or drew and
  the sub-emitter's particles never appeared; it is now cloned under the host and the reference re-pointed.
