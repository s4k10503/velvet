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

Because the attachment is physical and the tree is logical, the boundary behaves the same in
all three forms:

- **Context crosses.** A `V.Provider` above the portal call site is visible to the children.
- **Stores cross.** `UseStore` subscriptions are independent of panels.
- **Events do not cross.** UI Toolkit computes propagation from the physical tree; a handler on
  a logical ancestor never sees a portal child's events (documented deviation — there is no
  panel-spanning bubbling seam to hook).
- **Physical-walk styling does not cross.** Relational `group-`/`peer-` variants and
  focus-within resolve against the physical tree (and each panel has its own focus controller).
- **Responsive breakpoints are per-panel.** `sm:`…`2xl:` evaluate against the width of the panel
  the child is attached to, not the declaring panel.
- `dark:` is global and identical everywhere.

## Screen-space layers: `V.Portal(layer:)`

The framework owns one host panel per `UILayer` per mounted tree, created lazily on first use
and destroyed with the tree. When the declaring panel's settings are resolvable (a runtime
`UIDocument` panel), the host copies its theme, scaling and text settings and sorts around it;
a declaring panel without resolvable settings (an editor-hosted or headless root) gets an
empty runtime theme instead — native-control default visuals come from a theme, so declare
layers from a themed panel when those matter. The host object itself is hidden from the
Hierarchy (a framework-owned `UIDocument` host, like every other framework host):

| Layer | Sits | Typical use |
|---|---|---|
| `UILayer.Background` | below the app's main panel | backdrops, ambient chrome |
| `UILayer.Overlay` | above the main panel | floating panels, drag ghosts |
| `UILayer.Topmost` | above everything | toasts, modals, debug chrome |

One engine fact bounds this feature: **a screen-space panel always composites over the 3D
scene** — the compositor draws overlay panels after cameras, and `sortingOrder` only orders
panels among themselves. UI that must sit *among or behind scene geometry* is world-space
territory:

## World space: `V.WorldSpace`

```csharp
V.WorldSpace(position: signpost.position, rotation: signpost.rotation,
             panelSize: new Vector2(600, 200), children: …);
```

Each `V.WorldSpace` owns a world-space panel host (a framework-managed object positioned by
the given transform values) — the drei `<Html>` parity point. World-space panels are
**depth-tested**: scene geometry can occlude them and they can sit behind it, which no
screen-space layer can do. `position` / `rotation` updates on later renders move the live
host; `panelSize` is the panel's virtual resolution in pixels.

**Display-only for now**: world-space input routing (picking, focus) is not wired — treat
these panels as output. Interactive world-space UI is a separate milestone.
