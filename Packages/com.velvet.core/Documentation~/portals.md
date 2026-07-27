# Portals: registry targets, layer panels, and world space

Velvet has three ways to render children somewhere other than their position in the tree. All
three share one contract: **the children stay part of the logical tree** — context, state and
re-renders flow from the call site — while attaching physically elsewhere.

```csharp
V.Portal("modal-root", children: …);                 // into a registered element (same panel)
V.Portal(UILayer.Topmost, children: …);              // into a framework-managed layer panel
V.WorldSpace(anchor.position, children: …);          // into a world-space panel at a transform
```

## The shared boundary semantics

The boundary behaves the same in all three forms:

- **Context crosses.** A `V.Provider` above the portal call site is visible to the children.
- **Stores cross.** `UseStore` subscriptions are independent of panels.
- **`events:` handlers cross in all three portal forms**, through one synthetic-bubbling
  mechanism: `PointerDown`/`Up`/`Move`/`Enter`/`Leave`, `Wheel`, `KeyDown`/`Up`, and
  `FocusIn`/`Out` bindings on an `events:` prop bubble to the logical ancestor chain outside the
  portal boundary (React's own root-level event delegation, walking the logical parent chain
  rather than the DOM). For `V.Portal(targetId:)` the target's physical ancestors already receive
  the event through ordinary native bubbling and the bridge adds the LOGICAL chain on top; an
  element that is both — a physical ancestor of the target AND a logical ancestor of the call
  site — still fires exactly once. `ClickedBinding` (`Button`'s native click has no underlying
  event object to carry across a boundary) and `ChangeEventBinding<T>` (field value-change, same
  reason) stay physical-tree-only in every form, as do `FocusEvent`/`BlurEvent`: unlike
  `FocusInEvent`/`FocusOutEvent`, UI Toolkit dispatches those target-only with no bubble, so a
  bridge listener can never observe one raised on a descendant in the first place. See
  "Cross-panel input routing" below for what this shared mechanism does not cover.
- **Physical-walk styling does not cross, anywhere.** Relational `group-`/`peer-` variants and
  focus-within variants (`has-[:focus]:`, `group-focus-within:`) resolve against the physical
  tree in every portal form, including `V.Portal(layer:)`/`V.WorldSpace` — they register their
  own native focus/pointer callbacks directly rather than going through `events:`, so the
  synthetic cross-panel bridging above does not extend to them.
- **Responsive breakpoints are per-panel.** `sm:`…`2xl:` evaluate against the width of the panel
  the child is attached to, not the declaring panel.
- `dark:` is global and identical everywhere.

## Cross-panel input routing (`V.Portal(layer:)` / `V.WorldSpace`)

A framework-managed layer or world-space host is a completely separate UI Toolkit
`Panel`/`PanelSettings`/`UIDocument` from the panel its content logically belongs to — native
input delivery, propagation, and focus are all scoped per-panel by UI Toolkit itself. Velvet
closes three distinct gaps:

**Picking order.** When a screen-space layer panel visually overlaps the main panel, Unity's
own runtime input system claims to arbitrate delivery by `PanelSettings.sortingOrder`, but this
isn't reliable enough to depend on (a documented Unity Issue Tracker bug: a click can pass
through an overlapping `UIDocument`'s content to whatever sits behind it). Velvet arbitrates
this itself: before the main panel's own native dispatch processes a `PointerDown`/`Up`, it
walks every layer host in `sortingOrder` order and calls each panel's own `IPanel.Pick()` (which
resolves against that panel's own content alone, independent of any other panel's presence) —
the first host with actual content at that screen position wins, and the main panel's own
processing for that event is stopped. `V.WorldSpace` panels are NOT part of this arbitration.

**`V.WorldSpace` picking.** `RuntimePanelUtils.ScreenToPanel`/`CameraTransformWorldToPanel` look
like the natural tool for converting a screen position into a `PanelRenderMode.WorldSpace`
panel's local coordinates, but they are for UI Toolkit's OLDER RenderTexture-on-a-mesh workflow
and silently no-op (return the input essentially unchanged) against a Transform-driven
world-space panel — verified empirically, not just from docs.

The correct mechanism is Unity's own implicit runtime input system (bootstrapped automatically
every Play session, using Main Camera as the event camera and processing world-space input by
default, zero configuration required), which drives picking through an internal-only engine API a
package assembly cannot call. So Velvet's own job is limited to attaching a `BoxCollider` sized to
the panel's world extent (`panelSize` in pixels ÷ 100 pixels-per-unit, Unity's documented default)
to the host. Only the collider's placement is machine-verifiable (`Physics.Raycast` against it);
the end-to-end path is not, since Unity's runtime input system polls the real mouse device every
frame and batchmode has no way to drive that.

**Focus.** A focusable element inside a host panel is tracked correctly by that panel's own
`FocusController` when focused, and a host torn down while it holds focus hands focus back to
the main panel first (otherwise it would dangle on a destroyed `FocusController`, or — for a
layer panel, which persists — simply vanish since UI Toolkit clears `focusedElement` as soon as
the focused element leaves its panel's tree). Automatic Tab/Shift-Tab focus chaining ACROSS
panel boundaries is intentionally NOT implemented: UI Toolkit's own focus ring unconditionally
wraps within its own panel (confirmed from the engine source) and exposes no signal for "focus
tried to leave the ring". If you need this, wire it explicitly (e.g. a `KeyDownEvent` handler on
the portal's own boundary elements that calls `.Focus()` on the target panel's own focus target).

## Screen-space layers: `V.Portal(layer:)`

The framework owns one host panel per `UILayer` per mounted tree, created lazily on first use
and destroyed with the tree. When the declaring panel's settings are resolvable (a runtime
`UIDocument` panel), the host copies its theme, scaling (the DPI pair included) and text
settings, sorts around it, and keeps them in sync: a runtime change on the declaring panel (a
theme swap, a scale flip) re-copies on the next pass that touches the portal. A declaring panel
without resolvable settings (an editor-hosted or headless root) gets an empty runtime theme
instead — native-control default visuals come from a theme, so declare layers from a themed
panel when those matter. The host object itself is hidden from the Hierarchy:

| Layer | Sits | Typical use |
|---|---|---|
| `UILayer.Background` | below the app's main panel | backdrops, ambient chrome |
| `UILayer.Overlay` | above the main panel | floating panels, drag ghosts |
| `UILayer.Topmost` | above everything | toasts, modals, debug chrome |

One engine fact bounds this feature: **a screen-space panel always composites over the 3D
scene** — the compositor draws overlay panels after cameras, and `sortingOrder` only orders
panels among themselves. UI that must sit *among or behind scene geometry* is world-space
territory.

Layer order anchors to the **declaring panel's** `sortingOrder` (base −100 / +100 / +200), so
two mounted trees whose main panels share a `sortingOrder` produce layers that tie across the
apps — give each main panel its own base when several run side by side. A host panel killed
externally (a scene unload tearing down framework objects) is replaced the next time a portal
mounts on that layer; portals already mounted into the dead host need a remount.

## World space: `V.WorldSpace`

```csharp
V.WorldSpace(position: signpost.position, rotation: signpost.rotation,
             panelSize: new Vector2(600, 200), children: …);
```

Each `V.WorldSpace` owns a world-space panel host (a framework-managed object positioned by
the given transform values). World-space panels are **depth-tested**: scene geometry can
occlude them and they can sit behind it, which no screen-space layer can do. `position` /
`rotation` updates on later renders move the live host; `panelSize` is the panel's virtual
resolution in pixels. The host carries the `BoxCollider` described under "Cross-panel input
routing" above, which is what lets Unity's own runtime input system route pointer input into it.

A world-space host follows the same declaring-panel sync as the layers, and a host destroyed
externally (a scene unload) is skipped safely on later patches — remount the `V.WorldSpace`
node to rebuild it.

## Cross-panel Tab order: `PanelFocusOrder`

Both host flavors accept a `focusOrder:` argument. The default, `Isolated`, wraps the host
panel's focus ring internally so Tab never crosses the panel boundary. `Chained` joins the
declaring panel's Tab order at the portal's call site with iframe semantics — see the focus
guide for the full contract, including the one-tick deferral on the escape hop and why 2D
navigation never crosses panels.

## Screen-space anchored elements: `V.Anchored`

drei's `<Html>` parity in its DEFAULT mode: a plain screen-space element whose `left`/`top`
track a 3D scene Transform's projected position every frame, re-derived through
`RuntimePanelUtils.CameraTransformWorldToPanel` on the target's own camera (or `Camera.main`
when none is given). This is ordinary 2D UI drawn in the normal screen-space paint order, so it
has no inherent scene depth — it sits wherever its target currently projects to, unlike
`V.WorldSpace` above.

`occlude: true` opts into an explicit stand-in for the depth test: a
physics `Linecast` between the camera and the target hides the element when a solid
(non-trigger) collider sits between them, scoped by `occludeLayerMask` (a target whose own
collider sits on that mask will typically occlude itself — scope the mask to scene geometry that
excludes it). `distanceFactor` approximates perspective size falloff on otherwise-flat content:
it scales the element by itself divided by the current camera distance, so it is the reference
distance at which scale is exactly 1. Left unset, Anchored never touches the element's `scale`
style at all, so it composes freely with a `scale-*` class or a Motion scale variant; setting it
makes Anchored own that style slot every tick instead, so combining it with either of those is a
straightforward conflict, not an integration.

`V.Anchored` forces `position: absolute` inline (dynamic positioning has no other way to
work), so pass layout classes for everything else — sizing, background, text, and so on —
exactly as on any other element, at any nesting depth (the panel-space projection is
converted to the element's own parent-relative space before it's written, so it positions
correctly regardless of what ancestor containers sit between it and the panel root). The
optional pixel offset nudges the projected point (handy for centering a label rather than
pinning its top-left corner to it).

A null `target` (or one destroyed later) mounts an inert,
hidden element rather than throwing. The element also hides itself whenever its target sits
behind the camera rather than jumping to a wrong on-screen spot (drei's own
`isObjectBehindCamera` behavior); `hideWhenBehindCamera: false` opts out of the hide but does
not attempt to keep tracking a behind-camera target — there is no sensible projection for one,
so the element simply stays at its last resolved position.

Not supported: nesting `V.Anchored` inside a `V.WorldSpace` panel's children. That panel is
still a runtime (`Player`-context) panel — indistinguishable from an ordinary screen-space one
without reflecting into an internal engine property — so it silently receives the same
near-raw-world-space values `RuntimePanelUtils.CameraTransformWorldToPanel` is documented to
degrade to for a `V.WorldSpace` host (see above). `V.Anchored` targets an ordinary screen-space panel only.
