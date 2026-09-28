### Changed

- `V.Particles` draws every active particle system under the effect — child systems and sub-emitters as well
  as the root — each with its own renderer's texture, and a system whose renderer is disabled in the source,
  or renders in `ParticleSystemRenderMode.None`, draws nothing, the root included. It used to draw the root
  alone, whatever its renderer's state.

- `V.SceneView` renders its camera outside Play Mode, once per repaint tick while the camera is enabled and
  still targets the element's texture, so an editor-hosted SceneView shows the scene live. Feeding the
  texture there used to be the caller's job; a tool that still calls `camera.Render()` itself now renders the
  camera twice as often.
