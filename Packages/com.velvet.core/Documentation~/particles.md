# V.Particles & UseFrame: simulation-driven visuals

`V.Particles` draws a ParticleSystem's live simulation **inside an element**, as the geometry its
renderer builds, with no camera and no RenderTexture; handed a VFX Graph `VisualEffect` instead, it
shows what a camera renders of the effect ([below](#vfx-graph)). `UseFrame` is the hook underneath
that idiom: per-frame data that flows without touching component state.

```csharp
V.Particles(coinBurst, className: "absolute inset-0", playOn: PlayTrigger.Mount);
```

## The simulation-host contract

Hand `V.Particles` a ParticleSystem (typically a prefab reference); the framework owns the
rest:

- On mount, the effect is **cloned into a hidden host** at the source's world position and
  rotation (its renderers disabled, culling opted out so the invisible simulation never pauses,
  activated even when the source prefab is kept inactive; the source system is never touched or
  played). Collisions and triggers therefore meet the colliders and planes the source's would.
- `playOn: PlayTrigger.Mount` (default) starts the clone on mount; `PlayTrigger.Manual`
  instantiates it stopped for imperative control — call `Play()` / `Stop()` on the mounted
  `ParticlesElement` (reach it via `refCallback` or a query). Flipping `playOn` on a later
  render applies to the live host. A `playOn:` naming no `PlayTrigger` member is refused at
  construction: the factory throws `ArgumentOutOfRangeException`, naming the parameter.
- Swapping the `effect:` prop destroys the old host and clones the new effect; `null`
  destroys the host and leaves an inert box. Unmounting (conditional removal, type swaps,
  tree disposal included) destroys the host.
- `pixelsPerUnit` maps simulation world units to element pixels, centered on the element
  (`100` by default; must be positive — the factory throws otherwise).

Each repaint, every drawn system's renderer **bakes its geometry** — billboards, stretched
billboards, meshes, texture-sheet frames and trails, with each particle's current size, rotation
and color — for an orthographic camera looking down the root's forward axis, its view as tall as the
element, so a renderer's maximum particle size is measured against the element. The
geometry is drawn at the element's center in the root system's **local space** (up = element up),
whatever space each system simulates in. Particles draw with their renderer material's main
texture, or a Sprites-mode texture sheet's first sprite's texture; trails draw with the trail
material's. Each triangle is culled as its material's `_Cull` property says, and a material whose
shader declares no `_Cull` culls nothing.

Each vertex color is multiplied by its material's color: `_BaseColor` (URP), else twice `_TintColor`
(the legacy particle shaders), else `_Color`. A material composites as its blend factors say: one
whose `_DstBlend` is `One` adds its color weighted by its alpha, one with the multiply factors
(`DstColor`/`Zero`, or `Zero`/`SrcColor`) multiplies what lies beneath, and every other one, URP's
Premultiply mode among them, is alpha-blended. A shader that declares no `_SrcBlend`/`_DstBlend` is
read by name: Unity's Legacy Shaders/Particles/Additive and Mobile/Particles/Additive add, and its
Legacy Shaders/Particles/Multiply and Mobile/Particles/Multiply multiply. An additive or multiply
material draws through Velvet's `Velvet/ParticleBlend` shader, a UI Toolkit custom shader
([player-builds.md](player-builds.md#shaders) owns how it ships); while it cannot be found, those
materials are alpha-blended.

Systems draw by sorting layer, then order in layer, then render queue, then farthest first along the
bake camera's view, each renderer's sorting fudge added to its distance and the distance measured
from the system's own position, and in hierarchy order among ties. A system draws its particles
before its trails.

## What this path does and does not do

This is the **lightweight** path, matching Unity's own guidance that the Built-in Particle
System is the right tool for UI-scale effects:

- ✅ Emission, lifetime, velocity, size/color/rotation over lifetime, bursts, gravity —
  everything the simulation computes — for every particle alive.
- ✅ Renderer-module features: billboards and stretched billboards, mesh particles, texture-sheet
  animation and trails. Trails draw whenever the Trails module is enabled and the renderer has a
  trail material, render mode `None` included.
- ✅ Child systems, sub-emitters among them: every active system under the effect draws with
  its own renderer's materials, and a system whose renderer is disabled draws nothing, as in the
  scene. One set to render mode `None`, or to render meshes with none assigned, draws no particles.
  A sub-emitter that is not a child of the effect is cloned under the host too, at its own world
  position and rotation, so the clone's particles are the ones drawn. Collision and Trigger
  sub-emitters fire when their parent's particles meet the colliders, planes and trigger volumes
  the source's would.

The simulation runs anywhere the element does — including **outside Play Mode**: on
editor-hosted panels (preview tooling) the repaint tick steps the hidden host itself, so
effects play live without entering Play Mode.

## VFX Graph

```csharp
V.Particles(sparksVfx, className: "absolute inset-0", playOn: PlayTrigger.Mount);
```

A VFX Graph effect simulates and draws on the GPU, and `VisualEffect` has no call that bakes what it
draws into a mesh, so `V.Particles(VisualEffect visualEffect, …)` takes the camera route:

- On mount the effect is cloned into a hidden host at the source's pose, with a disabled
  orthographic camera under it looking down its forward axis, as tall in world units as the element
  is over `pixelsPerUnit`. The clone and everything under it are moved onto the highest layer from
  8 to 31 that the project has not named, the camera renders that layer alone, and every renderer
  under the clone is forced off (`Renderer.forceRenderingOff`) except while that camera renders, so
  no other camera draws the clone. A project that names every one of those layers leaves the clone
  on its source's layer, with a warning, and the camera then also draws whatever else stands on that
  layer in its view.
- On each repaint tick of a laid-out element the camera renders the clone into a RenderTexture of its
  size in device pixels (capped at 4096 per axis, one shared shrink), cleared to transparent black,
  and a layer over the element draws that texture across the element's box, composited as a
  premultiplied picture through `Velvet/ParticleBlend`. What the effect draws outside the box is not
  shown. The tick runs while the effect is mounted.
- `PlayTrigger.Mount` replays the clone with the source's initial event; `PlayTrigger.Manual`
  clears it and sends none. `Play()` sends the play event, and `Stop()` clears the clone without
  replaying it.

The ParticleSystem path draws through UI Toolkit alone; the VFX Graph path renders through the
project's render pipeline, which must be one VFX Graph supports. Velvet's own suites run under URP,
and no other pipeline is tested.

## UseFrame

```csharp
[Component]
static VNode Hud()
{
    var fps = Hooks.UseRef<FpsCounter>(() => new FpsCounter());
    Hooks.UseFrame(dt => fps.Current.Tick(dt));
    return V.Label(text: "…");
}
```

`Hooks.UseFrame(dt => …)` runs the callback once per frame with the elapsed seconds while
the component stays mounted, and stops on unmount. The seconds are measured on the mount's
`MountOptions.MotionClock` ([motion.md](motion.md#clocks-holding-motion-with-game-time) owns what each
clock gives). Four properties define it:

- **The latest closure always runs.** A re-render swaps the callback without re-subscribing,
  so captured state is never stale. Under the default compiler memoization hooks always run
  (only the VNode construction is cached), so the closure refreshes every render; only the
  opt-in `Memoize` props-bail skips renders entirely, and then the last rendered closure keeps
  running — the same freeze that bail applies to everything else.
- **No render per frame.** Per-frame data bypasses component state entirely; write to refs,
  imperative handles, or element styles from the callback. Setting state per frame would
  re-render the world every tick — that is the anti-pattern this hook exists to avoid.
- **Ordered across components.** An optional second argument, `priority` (default 0), orders
  callbacks within the same panel — lower runs earlier, equal priorities fall back to mount
  order — for the rare case where one `UseFrame` callback needs to read what another already
  wrote this same frame.
- **A positive priority takes rendering over**, as r3f's `useFrame(callback, renderPriority)`
  does. While a component with a positive priority is mounted and attached, each live
  [`V.SceneView`](scene-view.md) in the same panel holds its enabled camera disabled, re-enabling
  it once no such component is left, and the callback renders it:

  ```csharp
  Hooks.UseFrame(_ => effectCamera.Render(), priority: 1);
  ```

  The SceneView applies it on its recurring tick.

Frames tick while the component's host is attached to a panel and pause while it is not, and
that stays true across a keyed reorder of the host — the component's position in the panel's
firing order does not reshuffle just because its element moved.
