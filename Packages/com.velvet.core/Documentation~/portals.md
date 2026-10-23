# Portals: element and registry targets, layer panels, and world space

Velvet has four ways to render children somewhere other than their position in the tree. All
share one contract: **the children stay part of the logical tree** — context, state and
re-renders flow from the call site — while attaching physically elsewhere.

```csharp
V.Portal(container, children: …);                    // into an element you hold (same panel)
V.Portal("modal-root", children: …);                 // into a registered element (same panel)
V.Portal(UILayer.Topmost, children: …);              // into a framework-managed layer panel
V.WorldSpace(anchor.position, children: …);          // into a world-space panel at a transform
```

## Element or id

`V.Portal(container, …)` takes the container itself, the way `createPortal(children, container)`
does. Nothing is published and nothing is named, so two mounted trees in one process cannot collide,
and an element reached through a `refCallback` is a valid container without being registered first.

A ref attaches in the layout commit after the reconcile pass
([react-migration.md, lifecycle mapping](react-migration.md#3-lifecycle-mapping) states when), so a
container the same pass creates is not in the ref while that pass renders. Reading it for a portal's
target lands on the render after the one that created the container — the render a `UseState` write
from the `refCallback`, or the effect that follows the pass, asks for.

`V.Portal("modal-root", …)` is Velvet's own addition, with no `createPortal` counterpart: it resolves a
name through `FiberPortalRegistry`, one table for the whole process, so an id reaches a container across
unrelated call sites and mounted trees. Registering a registered name again to a different element warns and points it at the
element given, which moves the portals on it as described below; restating the same element is silent
and moves nothing.

**A different target moves the children**, in either form — the reconciler cannot patch one
container's portal into another's, so the old unmounts and the new mounts, which is what
`createPortal` does. Passing a different container, rendering a different id, and registering an
id again with a different element all move them, and the children arrive after whatever the new
container already held.

Registering an id is the one of those nothing would otherwise notice: a portal re-reads the registry
only when it is patched, and a registration causes no patch of its own. `Register` therefore asks the
components that declared the portals on that id to render again, and the move lands on that render
rather than inside the `Register` call. The same signal is what starts a portal declared before its
id existed: it warns at mount, and its children appear on the render the first registration asks for
rather than waiting for the declaring component to re-render for some reason of its own.
Registering the same element again, and
unregistering the id, both leave a live portal where it is — an unregistered id names nothing to move
to.

**A screen that registers an id from its `refCallback` and unregisters it from that callback's cleanup
hands the id to its replacement.** The reconcile path that replaces one screen with another creates
the arriving element before it removes the departing one, so what keeps the departing `Unregister`
from taking the arriving registration with it is the ref timing above.

Moving the portal is an unmount and a remount, so child state, refs and effects do not survive it.

**Moving a child across the boundary is one too**, in either direction: writing a component into a
portal's children that a render had outside them, or out of them to a position in the declaring
component's own tree, mounts a fresh instance on the far side and runs the departing one's cleanups.
[react-migration.md, what a position is](react-migration.md#what-a-position-is) states what a position
is; a portal's boundary is one, and it holds even where the two sides share a container.

**A container Velvet renders can hold children of its own beside a portal's**, in either form. The
portal's children follow the container's own, and when a render of the tree that declares the portal
changes how many of those there are — the element's own children, or a component among them rendering
again — the portal's range moves with them, so the next patch still lands on the portal's children. A
child other code appends behind the portal's children stays behind them. A portal declared among the
children of the container it targets is supported too. A portal nested inside another portal on the
same target is supported: a portal's own patch shifts the ranges that follow it.

## The shared boundary semantics

The boundary behaves the same in all four forms:

- **Context crosses.** A `V.Provider` above the portal call site is visible to the children.
- **Stores cross.** `UseStore` subscriptions are independent of panels.
- **`events:` handlers cross in every portal form**, through one synthetic-bubbling
  mechanism: `PointerDown`/`PointerUpEvent`/`Move`, `Wheel`, `KeyDownEvent`/`KeyUpEvent`, and
  `FocusIn`/`Out` bindings on an `events:` prop bubble to the logical ancestor chain outside the
  portal boundary (React's own root-level event delegation, walking the logical parent chain
  rather than the DOM). Pointer `Enter`/`Leave` bindings (`PointerEnterBinding`,
  `PointerLeaveBinding`) are not carried to a logical ancestor. A `Button`'s `onClick`
  (`ClickedBinding`) answers a click raised inside the portal, and a field's `onValueChanged`
  (`ChangeEventBinding<T>`) a value change of that type, the way React's `onClick` and `onChange` bubble out of a portal. Native dispatch also
  reaches the target's physical ancestors; an element that is one but no logical ancestor of the
  call site, the element the portal renders into included, runs none of its `events:` handlers for an
  event raised in the content, in either phase, whichever reconciler rendered the element, as the container of a React portal never sees them.
  That covers the `Button` `onClick`s and the field focus, blur, key and value-change bindings too. A
  `Button`'s click is decided the way a browser raises one, on the common ancestor of the press and the
  release: it runs unless both were in content off the Button's logical chain, and a submit follows the
  element it was raised on. An element that is both — a physical
  ancestor of the target AND a logical ancestor of the call site — still fires exactly once, except for a
  pointer-down or pointer-up with a physical-only `Button` between the content and that element: the
  Button's own click handling consumes both there, so the element does not hear them.
  A logical ancestor reached through portals nested in each other's content also fires once. A portal's child
  need not be a component: an element written straight into the portal bubbles the same way. `FocusEvent`/`BlurEvent` do not
  bubble, in a portal or out of one, as the DOM's `focus`/`blur` do not; React's bubbling
  `onFocus`/`onBlur` correspond to `FocusInEvent`/`FocusOutEvent`. See "Cross-panel input routing"
  below for what this shared mechanism does not cover. A capture binding (`Capture = true`) on a logical
  ancestor runs before the portal's content does, outermost first, as React's `on…Capture` handlers run
  ahead of a portal child's; a physical ancestor's runs through native dispatch, and only where it is also
  a logical ancestor. However many portals' content the target's physical path crosses, a logical ancestor runs once in
  each phase and the bridge runs nothing else, and a portal rendering into an element above its own call
  site runs the call site's bubble handlers before that element's. A pointer-down or pointer-up the layer
  router hands to a `V.Portal(layer:)`'s content reaches the content's logical ancestors in both phases
  too.
- **Physical-walk styling does not cross, anywhere.** Relational `group-`/`peer-` variants and
  focus-within variants (`has-[:focus]:`, `group-focus-within:`) resolve against the physical
  tree in every portal form, including `V.Portal(layer:)`/`V.WorldSpace` — they register their
  own native focus/pointer callbacks directly rather than going through `events:`, so the
  synthetic cross-panel bridging above does not extend to them.
- **Responsive breakpoints follow the declaring panel.** Unscoped `sm:`…`2xl:` in a
  `V.Portal(layer:)` or `V.WorldSpace` evaluate against the width of the panel the portal was
  declared on, the way a page's portals answer its one viewport. A layer host serves every portal
  on its layer in the tree and answers the panel of the last one to render into it; where that panel
  is itself a layer or world-space host, the panel that host's portal was declared on answers in
  turn. A container variant (`@md:`) is not affected: it asks the `@container` among the child's own
  ancestors. When a child resolves its width source is [styling-variants.md](styling-variants.md)'s to state.
- `dark:` is global and identical everywhere.

## Cross-panel input routing (`V.Portal(layer:)` / `V.WorldSpace`)

A framework-managed layer or world-space host is a completely separate UI Toolkit
`Panel`/`PanelSettings`/`UIDocument` from the panel its content logically belongs to — native
input delivery, propagation, and focus are all scoped per-panel by UI Toolkit itself. Velvet
closes three distinct gaps:

**Picking order.** When a screen-space layer panel visually overlaps the main panel, Velvet arbitrates
delivery itself: before the main panel's own native dispatch processes a `PointerDown`/`PointerUpEvent`, it
walks every layer host in `sortingOrder` order and calls each panel's own `IPanel.Pick()` (which resolves
against that panel's own content alone, independent of any other panel's presence) — the first host with
actual content at that screen position wins, and the main panel's own processing for that event is stopped.
`V.WorldSpace` panels are not part of this arbitration; their pointer input is Unity's own, below.

**`V.WorldSpace` picking.** A world-space host's `UIDocument` keeps a collider on the host's GameObject while a
Play session runs, and Unity's runtime input system picks and delivers through it. Velvet adds no collider of
its own. `PortalLayersPlaybackTests` pins that the host carries exactly the one
the document keeps.

**Focus.** A focusable element inside a host panel is tracked correctly by that panel's own
`FocusController` when focused, and a host torn down while it holds focus hands focus back to
the main panel first (otherwise it would dangle on a destroyed `FocusController`, or — for a
layer panel, which persists — simply vanish since UI Toolkit clears `focusedElement` as soon as
the focused element leaves its panel's tree). By default a host's elements follow the declaring
tree's own panel in Tab order, as a portal into `document.body` follows the rest of the document;
Velvet carries Tab across each panel boundary itself, predicting where the engine's own ring would
wrap rather than hooking it — see "Cross-panel Tab order: `PanelFocusOrder`" below.

## Screen-space layers: `V.Portal(layer:)`

The framework owns one host panel per `UILayer` per mounted tree, created lazily on first use
and destroyed with the tree. When the declaring panel's settings are resolvable (a runtime
`UIDocument` panel), the host copies its theme, scaling (the DPI pair included) and text
settings, sorts around it, and keeps them in sync: a runtime change on the declaring panel (a
theme swap, a scale flip) re-copies on the next pass that touches the portal. A declaring panel
without resolvable settings (an editor-hosted or headless root) gets an empty runtime theme
instead.

The host stands in for `body`, where a DOM portal's children sit. They match every stylesheet of the
document and the classes of `html` and `body`, but not the classes of the component that rendered them.
- **Stylesheets.** The host's root carries the stylesheets on the element the tree was mounted on and on its
  ancestors, in the order the declaring panel's cascade meets them. It leaves out any the host's panel
  already holds, such as the theme the settings copy put there. In an `EditorWindow` the carried sheets
  include the editor's default control sheet on the window's root. Velvet's utility stylesheet
  ([setup.md](setup.md)) goes on the host's root wherever those elements reach it, an `@import` included,
  but only while the host does not reach it some other way, such as a copied theme that imports it.
- **Classes.** The host's root takes the classes of the declaring panel's root and of the element directly
  under it, which is a `UIDocument`'s root: the `html` and `body` of that document. A `unity-` class is not
  taken. So `.theme-ocean { --color-primary: … }` reaches the portal's children when `theme-ocean` sits on
  the document's root, and does not when it sits on an element inside the page, as on the web.
- **`dark`.** The host's root follows `VelvetTheme.IsDark` when a root `VelvetStyleUtilities.BindThemeTo`
  bound (`AttachTo` binds one) is the mount element or one of its ancestors. Otherwise it copies whether
  one of those two document roots carries `dark`. A portal inside a `dark` section of a light page renders
  light.
- **What the host's root does not take from them.** Neither a carried `:root` rule, a document class, nor
  the copied theme's own `.unity-ui-document__root` rule paints the host's root, moves it, sizes it or moves
  its content. Its background, border, padding, margin, flex layout, opacity, display and overflow are held
  at their initial values inline, and its position, offsets and size at the values that fill its panel. A
  layer host's transform is held too; a world-space host keeps the size it was given and the transform its
  GameObject gives it. Custom and inherited properties still reach the portal's children. Every host root
  carries `unity-ui-document__root`, a world-space one included, so a carried rule on that class selects
  both.

All of this is looked at when a portal mounts into the host, whenever the portal is patched, and at each
update of the panel the tree was mounted on. So a sheet, an `@import` inside one, a document-root class, or
a theme binding added or removed there reaches the host at that panel's next update. Every portal a layer shares resolves the same.
The host object itself is hidden from the Hierarchy:

| Layer | Sits | Typical use |
|---|---|---|
| `UILayer.Background` | below the app's main panel | backdrops, ambient chrome |
| `UILayer.Overlay` | above the main panel | floating panels, drag ghosts |
| `UILayer.Topmost` | above everything | toasts, modals, debug chrome |

One engine fact bounds this feature: **a screen-space panel always composites over the 3D
scene** — the compositor draws overlay panels after cameras, and `sortingOrder` only orders
panels among themselves. UI that must sit *among or behind scene geometry* is world-space
territory. `PanelRenderModeContractTests` pins that Unity offers no render mode between the two.

Layer order anchors to the **declaring panel's** `sortingOrder` (base −100 / +100 / +200), so
two mounted trees whose main panels share a `sortingOrder` produce layers that tie across the
apps — give each main panel its own base when several run side by side. A host panel killed
externally (a scene unload tearing down framework objects) is replaced by the next portal on that layer to
mount or patch. The portals that were mounted into the dead host unmount and mount again into the
replacement, so their children's state does not survive it, as when an id names another element.

A `layer:` naming no `UILayer` member is refused at construction: `V.Portal` throws
`ArgumentOutOfRangeException` from the call itself, naming the parameter.

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

A world-space host follows the same declaring-panel sync and stylesheet carry as the layers,
and a host destroyed externally (a scene unload) is rebuilt by the node's next patch, which mounts the
children into the new host again; their state does not survive it.

## Cross-panel Tab order: `PanelFocusOrder`

Both host flavors accept a `focusOrder:` argument. The default, `Appended`, puts the host's elements
after the declaring tree's own panel in Tab order; `Chained` puts them at the portal's call site with
iframe semantics; `Isolated` keeps Tab wrapping within the host panel. The focus guide's
[cross-panel Tab order](focus.md#cross-panel-tab-order-panelfocusorder) section owns the contract.

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
excludes it). `distanceFactor` scales the element as drei's does: by the factor divided by the world height the camera
sees at the target, which is 2·tan(fov/2)·distance for a perspective camera and 2·`orthographicSize` for an
orthographic one, whose view does not widen with distance. Left unset, Anchored never touches the element's
`scale` style at all, so it composes freely with a `scale-*` class or a Motion scale variant; setting it makes
Anchored own that style slot every tick instead, so a scale of the same element's own conflicts with it. drei
keeps the content in an element inside the wrapper it scales; a scale of your own goes on a child of the
Anchored element likewise.

`V.Anchored` forces `position: absolute` inline (dynamic positioning has no other way to
work), so pass layout classes for everything else — sizing, background, text, and so on —
exactly as on any other element, at any nesting depth (the panel-space projection is
converted to the element's own parent-relative space before it's written, so it positions
correctly regardless of what ancestor containers sit between it and the panel root). The
optional pixel offset nudges the projected point (handy for centering a label rather than
pinning its top-left corner to it).

A null `target` (or one destroyed later) mounts an inert,
hidden element rather than throwing. The element also hides itself whenever its target sits
behind the camera (drei's own `isObjectBehindCamera` behavior); `hideWhenBehindCamera: false`
opts out of the hide, and the element keeps following the target's projection there, as drei's
`<Html>` does once `onOcclude` takes the hide over.

In an editor panel (Velvet content in an `EditorWindow`) the camera's viewport is laid over the
panel, which is how drei's `<Html>` places a projection on its canvas. Among a `V.WorldSpace`
panel's children, the element sits where the camera's ray to the target crosses that panel's plane,
and hides where the ray does not cross it.
