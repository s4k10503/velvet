# Drag and drop: the dnd-kit parity guide

Velvet's drag-and-drop layer models [dnd-kit](https://dndkit.com/)'s core — `DndContext`,
`useDraggable`, `useDroppable`, `DragOverlay`, and pluggable collision detection — on top of UI
Toolkit's own pointer pipeline. Everything rides the element-binding discipline the rest of the
framework uses: settings live on element props, the reconciler owns attach/update/detach, and a
pooled element leaves with everything the session ever wrote restored.

## The pieces

- **`V.DndContext`** — the scope, dnd-kit's `DndContext`. A real container element (the same
  decision as `V.FocusScope`): draggables and droppables pair with their nearest ancestor context
  at event time, and one drag may be active per mounted tree at a time. Carries the four
  callbacks (`onDragStart`, `onDragOver`, `onDragEnd`, `onDragCancel`), the collision strategy,
  and a scope-wide default activation constraint.
- **`V.Draggable(id:)`** — a drag source, dnd-kit's `useDraggable`. The element itself is the
  drag node. `dragData:` carries an arbitrary payload into the callbacks; `movement:` chooses
  whether the element itself follows the pointer (`Translate`, the default — an inline
  `translate` written every move and restored afterward) or stays put (`None` — the
  `V.DragOverlay` ghost pattern); `whileDraggingClass:` is the zero-re-render styling channel for
  the dragging state.
- **`V.Droppable(id:)`** — a drop target, dnd-kit's `useDroppable`. `whileOverClass:` applies
  while it is the winning collision; `whileDragActiveClass:` applies to every enabled candidate
  while any drag is live in the scope (dnd-kit's droppable `active` cue). `dropData:` rides into
  the callbacks for accept-filtering, which stays app logic — exactly as in dnd-kit core.
- **`V.DragOverlay`** — the portal-rendered drag preview, dnd-kit's `DragOverlay`. Expands to a
  `V.Portal(UILayer.Overlay)` hosting a framework-positioned, picking-ignored container that is
  sized to the source at activation and tracks the pointer while a drag is active (hidden
  otherwise). What renders INSIDE it is ordinary user state — set an "active item" in
  `onDragStart`, clear it in `onDragEnd`/`onDragCancel`, and render the preview conditionally:
  dnd-kit's own `activeId` recipe.

Any existing element can be a source, target, or scope through the corresponding
`FiberElementProps` slots — the factories are sugar. An element carrying both `Draggable` and
`Droppable` under the same id (the sortable-row shape) never collides with itself.

## Activation: clicks keep working

A press becomes a drag only after crossing an activation constraint (`DragActivation`). The
default is 4 px of travel — a deliberate, documented deviation from dnd-kit's unconstrained
`PointerSensor` default, because UI Toolkit's `Clickable` captures the pointer at pointer-down
and a zero threshold would kill clicks on draggable buttons. A sub-threshold release is a plain
click, completely untouched. `DragActivation.None` restores the raw dnd-kit behavior (the press
is the drag); a `DelaySec` greater than zero switches to hold-to-drag (dnd-kit's either/or):
activation after the hold, aborted if travel exceeds `Tolerance` first.

After a REAL drag, the release is swallowed before `Clickable` sees it, so a draggable button
does not also fire `clicked` — and the press-derived styling state (`whileTap`, `active:`) is
settled synthetically, since the real pointer-up never reaches those listeners.

One engine ground truth to know: while an element holds pointer capture, captured pointer events
are delivered to that element only. An interactive CHILD that captures at its own pointer-down (a
button inside a draggable card) therefore blacks out the draggable's view of the gesture —
interactive capturing children are non-drag zones in distance mode. Put the `Draggable` setting
on the interactive element itself (a draggable `V.Button` works: the captured events land on the
same element), or use delay activation.

## Collision detection

The strategy is a delegate — `DndCollisionDetection` — receiving the active rect (the source's
activation-time rect translated by the pointer delta: dnd-kit's translated-initial-rect
semantics, correct even under `Movement.None`), the pointer position, and the pre-filtered
candidates (in-scope, enabled, same panel, self excluded). Built-ins on `DndCollisions`:
`RectIntersection` (largest overlap wins — the dnd-kit default), `ClosestCenter` (the sortable
workhorse), and `PointerWithin` (pointer containment, innermost wins). A custom strategy is just
a delegate; it must be pure over the query, since it runs on every move.

Candidate rects are read live from the panel every move, so mid-drag layout shifts, scrolls, and
droppables mounting/unmounting are picked up automatically — dnd-kit's `MeasuringConfiguration`
has no equivalent because nothing is cached to configure.

## Callbacks and state

`onDragStart`, `onDragEnd`, and `onDragCancel` run in the same discrete-input bracket as click
handlers: state written there commits synchronously in the same frame — a sortable list's
`onDragEnd` reorder is visible immediately, and wrapping rows in `V.Motion(layoutId:)` makes the
post-drop reorder animate with no extra wiring. `onDragOver` fires only when the winning target
CHANGES (including to null) and stays on the normal frame-boundary lane, since it can fire every
move.

`isDragging` / `isOver` are deliberately NOT re-rendering booleans: the `while*Class` channels
cover styling with zero re-renders, and structural state goes through the callbacks plus your own
`UseState` — dnd-kit's own recipe. Escape cancels an active drag; a cancelled (or torn-down) drag
restores the source's inline translate and every applied class by construction.

## Scope cuts (each deliberate, with its dnd-kit name)

- **Keyboard sensor + accessibility announcements** (`KeyboardSensor`, `announcements`) — pointer
  only for now; keyboard DnD couples to the focus layer and is its own follow-up. Escape-cancel
  IS included.
- **Sensors abstraction** (`sensors`) — one implementation behind an abstraction is indirection;
  a keyboard path would land as a second internal attachment, not a plugin API.
- **`useDraggable`/`useDroppable` hooks returning re-rendering flags** — layerable later over the
  same registries; the class channels and callbacks cover the pillars.
- **Cross-panel drag** — source and droppables must share one panel; collision skips
  foreign-panel candidates. Content inside `V.Portal(layer:)` / `V.WorldSpace` gets working DnD
  within its own panel; the `DragOverlay` ghost is the one sanctioned cross-panel piece
  (display-only, picking-ignored).
- **Sortable preset** (`@dnd-kit/sortable`) — a separate package upstream, a separate issue here;
  `V.Motion(layoutId:)` already animates the post-drop reorder.
- **`dropAnimation`** on the overlay, **`modifiers`** (axis/bounds restriction), and
  **auto-scroll** near container edges — additive later.
- **`useDndMonitor` / `useDndContext`** introspection — no consumer yet.
- **Ranked `Collision[]`** — a strategy returns a single winner; the context only ever consumed
  the first collision anyway.
- **Multi-touch / concurrent drags** — one active drag per mounted tree (dnd-kit's `active` is
  singular too); extra pointer-downs during a session do not arm.

Composition caveats, documented rather than solved: draggables inside an engine `ListView` with
`reorderable: true` conflict with its internal drag processor (unsupported); the delta is
panel-space, so a scaled ancestor skews tracking (a caveat dnd-kit shares); and the
interactive-capturing-children rule above.
