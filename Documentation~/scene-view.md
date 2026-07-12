# V.SceneView: a camera's output as an element

`V.SceneView` displays a `Camera`'s rendered output inside the UI tree — the `<canvas>`
parity point. Hand it a camera; the framework owns everything else.

```csharp
V.SceneView(previewCamera, className: "w-64 h-64 rounded-lg border border-neutral-700");
```

## The RenderTexture contract

There is no RenderTexture in the API — the framework owns it:

- On layout, a RenderTexture is created at the element's **laid-out size in device pixels**
  (the panel's scale factor is included, times `resolutionScale`, capped at 4096 per axis) and
  assigned to `camera.targetTexture`; the element shows it as its background image.
- A geometry change (the element resizes) recreates the texture at the new size and re-targets
  the camera. A zero-sized or unattached element holds no texture.
- Swapping the `camera:` prop releases the old camera and targets the new one; passing `null`
  releases everything and leaves an inert box.
- Unmounting (including a conditional `cond ? V.SceneView(...) : null` removal and whole-tree
  disposal) releases the camera's target and destroys the texture.
- Release is **polite**: if user code reassigned `camera.targetTexture` after mount, unmount
  leaves that assignment intact.

## Styling composes

The output arrives through the element's background image, so the utility classes you would
put on any box apply to the camera picture too — `rounded-*` corners clip it, `border-*`
frames it, and sizing/layout utilities drive the render resolution itself.

`resolutionScale` decouples render cost from display size: `resolutionScale: 0.5` renders the
camera at half the element's pixel size (the background scales it up). A non-positive or NaN
scale throws at the factory.

One property is spoken for: **the element's background image belongs to the camera output
while a camera is bound**. A `BackgroundImage` passed through `styles:` (or a `bg-[...]`
utility) shows only until the camera texture arrives — use a sibling/overlay element for
poster or overlay imagery.

## Live output

The element samples the camera's RenderTexture at draw time — camera motion and scene changes
appear without any Velvet re-render. A `UseState`/store update is only needed when the
*element* changes (size, camera identity), never per frame.

Runtime panels redraw continuously, so this is free. An **editor-hosted panel** repaints only
when dirty, so a bound SceneView drives a small recurring repaint tick there; note that
outside Play Mode a camera does not render on its own — feeding the texture (e.g. calling
`camera.Render()` from your tool's update) is the caller's job.

## Notes

- One camera renders into one SceneView at a time: mounting a second element with the same
  camera re-targets it, and the earlier element freezes on its last rendered frame. Give each
  SceneView its own camera.
- The camera keeps rendering while targeted (its own `enabled` flag is yours to manage —
  disable cameras whose output is currently unnecessary).
- URP is the supported pipeline (this project validates against URP; capture rides
  `camera.targetTexture`, which is pipeline-agnostic — no Built-in-only hooks are used).
