### Changed

- `V.Particles` draws the geometry each drawn system's renderer bakes instead of one quad per particle: mesh
  particles, stretched billboards, texture-sheet frames and trails now draw, trails with the trail material's
  texture and a Sprites-mode texture sheet with its first sprite's texture, and each triangle is culled as its
  material's `_Cull` property says. A particle is held to its renderer's maximum size against the element.
- The hidden host of a `V.Particles` element stands at the source's world position and rotation instead of far
  below the scene, and a sub-emitter cloned from outside the effect keeps its own world position and rotation,
  so Collision and Trigger sub-emitters fire against the colliders, planes and trigger volumes the source's
  would.
- `V.SceneView` shows the camera through a child laid over the element's content box instead of through the
  element's background image. The element's own background image stays beneath the picture while the camera is
  live, the texture is sized to the content box rather than the laid-out box, and each corner of the picture is
  rounded along the content edge.
- Several `V.SceneView` elements can show one camera: while the camera is enabled, an element it does not target
  renders it into its own texture on its tick, in Play Mode too, and an element whose camera targets nothing
  claims it on its tick.
- A `Hooks.UseFrame` callback with a positive priority takes rendering over, as react-three-fiber's `useFrame`
  does: while it is mounted and attached, a `V.SceneView` in the same panel holds its enabled camera disabled,
  and enables it again once no such callback is left.
