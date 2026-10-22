# V.SceneView: a camera's output as an element

`V.SceneView` displays a `Camera`'s rendered output inside the UI tree — the `<canvas>`
parity point. Hand it a camera; the framework owns everything else.

```csharp
V.SceneView(previewCamera, className: "w-64 h-64 rounded-lg border border-neutral-700");
```

## The RenderTexture contract

There is no RenderTexture in the API — the framework owns it:

- On layout, a RenderTexture is created at the element's **content-box size in device pixels**
  (the panel's scale factor is included, times `resolutionScale`, capped at 4096 — one shared
  shrink when either axis overflows, so the aspect is preserved) and assigned to
  `camera.targetTexture`; a child the framework lays over the content box shows it.
- A geometry change (the element resizes) recreates the texture at the new size and re-targets
  the camera. A zero-sized or unattached element holds no texture. A pixel-density change (a
  monitor DPI move, a panel-scale change) can leave the element's geometry where it was; the
  element's recurring tick re-derives the texture then, on every panel type.
- Swapping the `camera:` prop releases the old camera and targets the new one; passing `null`
  releases everything and leaves an inert box.
- Unmounting (including a conditional `cond ? V.SceneView(...) : null` removal and whole-tree
  disposal) releases the camera's target and destroys the texture.
- Release is **polite in both directions**: if user code reassigned `camera.targetTexture`
  after mount, unmount leaves that assignment intact — and a layout- or tick-driven resync
  never claws it back either (only an explicit camera/settings pass claims the camera; a bare
  resync reclaims it from `null` alone).

## Styling composes

The output covers the element's content box, as a `<canvas>` bitmap does, so the utility classes
you would put on any box compose with the camera picture: its own background (a color, a
`BackgroundImage` passed through `styles:`, a gradient or `bg-[...]` utility) paints beneath the
picture, `border-*` frames it, padding insets it, `rounded-*` corners clip it along the content
edge (each corner's radius less the border and padding beside it, the larger side's where the two
differ), and sizing/layout utilities drive the render resolution itself.

`resolutionScale` decouples render cost from display size: `resolutionScale: 0.5` renders the
camera at half the content box's pixel size (the picture is scaled up to the box). A non-positive
or NaN scale throws at the factory.

## Live output

The element samples the camera's RenderTexture at draw time — camera motion and scene changes
appear without any Velvet re-render. A `UseState`/store update is only needed when the
*element* changes (size, camera identity), never per frame.

A bound SceneView drives a small recurring tick while its texture is live. On an
**editor-hosted panel**, which repaints only when dirty, it marks the element dirty, and **outside
Play Mode** it calls `camera.Render()`, so a tool previewing a scene shows it live without
entering Play Mode. The tick renders only a camera that is enabled, and leaves alone a camera user
code pointed at a texture of its own.

## Notes

- Several SceneViews can show one camera. The element that claimed it last holds
  `camera.targetTexture`; while the camera is enabled, each other element's tick, in Play Mode
  too, points the camera at its own texture, renders it and points it back, and once the holder
  releases the camera, the next tick of another element showing it claims it.
- The camera keeps rendering while targeted (its own `enabled` flag is yours to manage —
  disable cameras whose output is currently unnecessary), except while a positive-priority
  `UseFrame` callback in the panel takes rendering over
  ([particles.md](particles.md#useframe) owns that rule).
- The render pipeline is whichever the project uses: the element sets `camera.targetTexture`
  and calls `camera.Render()`, and calls no pipeline-specific API. Velvet's own suites run
  under URP.
