### Changed

- `V.Particles` draws the geometry each drawn system's renderer bakes instead of one quad per particle: mesh
  particles, stretched billboards, texture-sheet frames and trails now draw, trails with the trail material's
  texture and a Sprites-mode texture sheet with its first sprite's texture, and each triangle is culled as its
  material's `_Cull` property says. A particle is held to its renderer's maximum size against the element.
- `V.Particles` draws as each particle material says: its color (`_BaseColor`, twice a legacy shader's `_TintColor`,
  or `_Color`) multiplies each vertex color, and a material whose factors add or multiply composites that way over
  what lies beneath, through a new `Velvet/ParticleBlend` UI Toolkit shader, where it used to be alpha-blended.
  Systems draw by sorting layer, order in layer, render queue and distance with each renderer's sorting fudge
  added, where they used to draw in hierarchy order.
- `V.Particles` draws a system's trails whenever its Trails module is enabled with a trail material, a renderer in
  render mode `None` included, and draws no trails where the renderer has no trail material.
- `V.Particles(null, …)` with a positional `null` no longer compiles, since `V.Particles` also takes a
  `VisualEffect`; cast the `null` to `ParticleSystem` or name the argument `effect:`.
- The hidden host of a `V.Particles` element stands at the source's world position and rotation instead of far
  below the scene, and a sub-emitter cloned from outside the effect keeps its own world position and rotation,
  so Collision and Trigger sub-emitters fire against the colliders, planes and trigger volumes the source's
  would.
- `V.SceneView` shows the camera through a child laid over the element's content box instead of through the
  element's background image. The element's own background image stays beneath the picture while the camera is
  live, the picture stays beneath every other child of the element, the texture is sized to the content box
  rather than the laid-out box, and each corner of the picture is rounded along the content edge.
- Several `V.SceneView` elements can show one camera: while the camera is enabled, an element it does not target
  renders it into its own texture on its tick, in Play Mode too, and an element whose camera targets nothing
  claims it on its tick.
- A `Hooks.UseFrame` callback with a positive priority takes rendering over, as react-three-fiber's `useFrame`
  does: while it is mounted and attached, a `V.SceneView` in the same panel holds its enabled camera disabled,
  and enables it again once no such callback is left, and before the editor saves a scene or reloads assemblies.
