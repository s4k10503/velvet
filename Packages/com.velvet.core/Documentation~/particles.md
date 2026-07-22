# V.Particles & UseFrame: simulation-driven visuals

`V.Particles` draws a ParticleSystem's live simulation as textured quads **inside an
element** — no camera, no RenderTexture, no render-pipeline coupling. `UseFrame` is the hook
underneath that idiom: per-frame data that flows without touching component state.

```csharp
V.Particles(coinBurst, className: "absolute inset-0", playOn: PlayTrigger.Mount);
```

## The simulation-host contract

Hand `V.Particles` a ParticleSystem (typically a prefab reference); the framework owns the
rest:

- On mount, the effect is **cloned into a hidden host** (its renderer disabled, culling opted
  out so the invisible simulation never pauses, activated even when the source prefab is kept
  inactive — only the simulation is consumed; the source system is never touched or played).
- `playOn: PlayTrigger.Mount` (default) starts the clone on mount; `PlayTrigger.Manual`
  instantiates it stopped for imperative control — call `Play()` / `Stop()` on the mounted
  `ParticlesElement` (reach it via `refCallback` or a query). Flipping `playOn` on a later
  render applies to the live host.
- Swapping the `effect:` prop destroys the old host and clones the new effect; `null`
  destroys the host and leaves an inert box. Unmounting (conditional removal, type swaps,
  tree disposal included) destroys the host.
- `pixelsPerUnit` maps simulation world units to element pixels, centered on the element
  (`100` by default; must be positive — the factory throws otherwise).

Particles draw at the element's center in **local simulation space** (world up = element up).
A world-space source warns on mount and is read as local. Quads are tinted with each
particle's current color and rotated by its 2D rotation; the texture comes from the effect's
renderer material.

## What this path does and does not do

This is the **lightweight** path, matching Unity's own guidance that the Built-in Particle
System is the right tool for UI-scale effects:

- ✅ Emission, lifetime, velocity, size/color/rotation over lifetime, bursts, gravity —
  everything the simulation computes.
- ❌ Renderer-module features: trails, mesh particles, texture-sheet animation, sub-emitter
  rendering, stretched billboards. One texture per system; up to 2048 particles drawn.

The simulation runs anywhere the element does — including **outside Play Mode**: on
editor-hosted panels (preview tooling) the repaint tick steps the hidden host itself, so
effects play live without entering Play Mode.

## What about VFX Graph?

VFX Graph is GPU-resident — there is no supported per-particle CPU readback, so the
quad-transfer path cannot consume it. **Its supported path in Velvet is composition with
[`V.SceneView`](scene-view.md)**: put the effect on a dedicated layer, point a culling-masked
camera at it, and mount `V.SceneView(effectCamera)`. That renders anything (VFX Graph,
trails, meshes, sheets) at the cost of a camera + RenderTexture per view.

| | `V.Particles` | `V.SceneView` |
|---|---|---|
| Systems | Built-in ParticleSystem | anything a camera can see (VFX Graph included) |
| Cost | quads in the element itself | camera + RenderTexture + layer isolation |
| Pipeline | independent of the render pipeline | renders through URP |
| Renderer features | simulation only | all of them |

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
the component stays mounted, and stops on unmount. Three properties define it:

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
  wrote this same frame. Unlike r3f's `useFrame(callback, renderPriority)`, a positive priority
  has no side effect beyond ordering; Unity's own rendering has no internal loop for it to take
  over.

Frames tick while the component's host is attached to a panel and pause while it is not, and
that stays true across a keyed reorder of the host — the component's position in the panel's
firing order does not reshuffle just because its element moved.
