# Drag and drop: the dnd-kit parity guide

Velvet's drag-and-drop layer models [dnd-kit](https://dndkit.com/)'s core — `DndContext`,
`useDraggable`, `useDroppable`, `DragOverlay`, sensors, modifiers, auto-scroll, pluggable collision
detection and screen-reader announcements — and its `@dnd-kit/sortable` preset, on top of UI
Toolkit's own pointer and keyboard pipeline. Everything rides the element-binding discipline the rest of the
framework uses: settings live on element props, the reconciler owns attach/update/detach, and a
pooled element leaves with everything the session ever wrote restored.

## The pieces

- **`V.DndContext`** — the scope, dnd-kit's `DndContext`. A component rendering a real container
  element, which provides the drag state to the hooks below it: draggables and droppables pair with their
  nearest ancestor context at event time, and one drag may be active per mounted tree at a time. Carries
  the five callbacks (`onDragStart`, `onDragMove`, `onDragOver`, `onDragEnd`, `onDragCancel`), the
  collision strategy, a scope-wide default activation constraint, and `sensors:`, `modifiers:`,
  `autoScroll:` and `accessibility:`. A scope declared through the `FiberElementProps.DndContext` slot
  behaves the same way, but the hooks do not see it.
- **`V.Draggable(id:)`** — a drag source, dnd-kit's `useDraggable`. The element itself is the
  drag node. `dragData:` carries an arbitrary payload into the callbacks; `movement:` chooses
  whether the element itself follows the pointer (`Translate`, the default — an inline
  `translate` written every move and restored afterward) or stays put (`None` — the
  `V.DragOverlay` ghost pattern); `whileDraggingClass:` is the zero-re-render styling channel for
  the dragging state. A `movement:` naming no `DragMovement` member is refused at construction:
  the factory throws `ArgumentOutOfRangeException`, naming the parameter.
- **`V.Droppable(id:)`** — a drop target, dnd-kit's `useDroppable`. `whileOverClass:` applies
  while it is the winning collision; `whileDragActiveClass:` applies to every enabled candidate
  while any drag is live in the scope (dnd-kit's droppable `active` cue). `dropData:` rides into
  the callbacks for accept-filtering, which stays app logic — exactly as in dnd-kit core.
- **`V.DragOverlay`** — the drag preview, dnd-kit's `DragOverlay`. A component rendering a
  framework-positioned, picking-ignored container through `V.Portal(UILayer.Overlay)`, or in place in a
  mount on an editor panel (an `EditorWindow`), whose layer panel is the Game view's rather than the
  window's. The container is sized to the source at activation, follows the drag while one is active, and
  is hidden otherwise. What renders INSIDE it is ordinary user state — set an "active item" in
  `onDragStart`, clear it in `onDragEnd`/`onDragCancel`, and render the preview conditionally: dnd-kit's
  own `activeId` recipe. Every overlay declared under the dragging scope, mounted before the drag or
  during it, shows the preview, as every dnd-kit `DragOverlay` renders its children while its own
  context's drag is active; an overlay under another scope stays hidden. `dropAnimation:` and
  `modifiers:` are below.
- **`V.SortableContext` / `V.Sortable`** — the sortable preset; see [Sortable lists](#sortable-lists).

Any existing element can be a source, target, or scope through the corresponding
`FiberElementProps` slots — the factories are sugar. An element carrying both `Draggable` and
`Droppable` under the same id never collides with itself, except a `V.Sortable` item, whose drag over its
own slot is over itself, as a dnd-kit sortable item's is.

## Sensors

A scope offers each pointer-down and key-down that reaches one of its enabled draggables to its
`sensors:` in order, and the first that claims the event starts the drag — dnd-kit's `sensors`. A press
or key inside a nested draggable is offered to the innermost one only, and one inside a `NoDrag` element
to no draggable enclosing it. Null means `DndSensors.Default`: a `DndPointerSensor` and a `DndKeyboardSensor`, dnd-kit's
default pair; an empty list starts no drag.

- **`DndPointerSensor(activation:)`** — dnd-kit's `PointerSensor`, the press path the next section
  describes. Its `activation:` applies where the draggable declares none, over the scope's.
- **`DndKeyboardSensor(codes:, coordinateGetter:)`** — dnd-kit's `KeyboardSensor`. A start key (Space or
  Enter) on a draggable, or on an element inside one that holds keyboard focus, starts the drag; an end key
  (Space, Enter or Tab) drops it and a cancel key (Escape) cancels it. Each other key the
  `coordinateGetter` answers moves the drag to the coordinates it returns; the default,
  `DndKeyboardSensor.DefaultCoordinates`, moves 25 px per arrow key. While the drag lasts, the navigation
  move and submit events its keys raise are held back, so they neither move focus off the source nor click
  it. Collision strategies get no pointer from it (`DndCollisionQuery.HasPointer` is false), so
  `PointerWithin` collides with nothing, as in dnd-kit, and it never auto-scrolls.
- **A custom sensor** derives from `DndSensor` and overrides `TryActivate(evt, activation)`, returning true
  to claim the event. `activation.Start(coordinates, onClose)` starts the drag and returns a
  `DndSensorSession`, whose `Move(coordinates)` translates the drag by its distance from the start's,
  and whose `End()` and `Cancel()` drop or cancel it; `onClose` runs once when the drag ends for any reason.
  A drag started from a pointer-down event gives collision detection a pointer: the press, moved by
  each `Move` as far as the session moved.

## Activation

With no activation constraint declared, the press is the drag, as with dnd-kit's `PointerSensor`
default: a press on a draggable button starts a drag and does not click it. An activation
constraint (`DragActivation`) keeps clicks working: `new DragActivation(Distance: 4f)` makes a
press a drag only after 4 px of travel, and a sub-threshold release is a plain click, completely
untouched. A `DelaySec` greater than zero switches to hold-to-drag (dnd-kit's either/or):
activation after the hold, aborted if travel exceeds `Tolerance` first. `DragActivation.None` is
the unconstrained default, for a draggable that overrides a constrained scope.

After a REAL drag, the release is swallowed before `Clickable` sees it, so a draggable button
does not also fire `clicked` — and the press-derived styling state (`whileTap`, `active:`) is
settled synthetically on the pressed element, the draggable and its ancestors, since the real
pointer-up never reaches those listeners.

A press on an interactive child that captures the pointer at its own pointer-down (a button
inside a draggable card) drags the card once the constraint is met, and the child's click is
aborted; a sub-threshold release stays the child's click. The same holds for an interactive
ancestor that captures (a draggable inside a button). A control that has a pointer gesture of
its own — a slider, a scroller, a text field's selection — loses it to the drag the same way. Set
`FiberElementProps.NoDrag` on the control or on an element around it
(`V.Div(props: new FiberElementProps { NoDrag = true }, children: ...)`) and a press inside it
never arms that element's own draggable or an enclosing one. A draggable nested inside a `NoDrag`
element still arms. That is this layer's spelling of dnd-kit's opt-out, a child that
stops or prevents its pointer-down: a draggable arms before any of its children's own pointer-down
callbacks run.

When draggables nest, the innermost enabled one under the press takes it and the outer ones do not
arm, as dnd-kit's innermost activator claims the press.

## Collision detection

The strategy is a delegate — `DndCollisionDetection` — receiving the active rect (the source's
activation-time rect translated by the drag's translate: dnd-kit's translated-initial-rect
semantics, correct even under `Movement.None`), the pointer position, and the pre-filtered
candidates (in-scope, enabled, on a reachable panel, self excluded but for a `V.Sortable` item), and returns a ranked `DndCollision[]`
(`Id`, `Data`, and the `Value` it ranked by) with the best first, as dnd-kit's `CollisionDetection`
does; the first entry becomes `over` and an empty array means no collision. Built-ins on
`DndCollisions` follow the dnd-kit algorithm of the same name, including its four-decimal rounding where it
rounds and its tie order (equal values keep registration order, and a droppable that unmounts and mounts
again registers last, as in dnd-kit's droppable container map):

| Strategy | Collides | Ranked by |
| --- | --- | --- |
| `RectIntersection` (the default) | droppables overlapping the active rect | intersection ratio `i / (droppable + active - i)`, greatest first |
| `ClosestCenter` (the sortable workhorse) | every droppable | distance between centers, nearest first |
| `ClosestCorners` | every droppable | mean distance between the four corresponding corners, nearest first |
| `PointerWithin` | droppables whose rect contains the pointer, edges included | mean distance from the pointer to the four corners, nearest first |

`DragOverArgs.Collisions` and `DragEndArgs.Collisions` carry the strategy's full ranking at the latest move, as
dnd-kit's drag events carry `collisions`. `ClosestCenter` and `ClosestCorners` leave out a droppable whose rect has
a NaN edge. A custom strategy is just a delegate; it must be pure over the query, since it runs on every move, and
may return `null` or an empty array for no collision.

Candidate rects are read live from the panel every move, so mid-drag layout shifts, scrolls, and
droppables mounting/unmounting are picked up automatically — dnd-kit's `MeasuringConfiguration`
has no equivalent because nothing is cached to configure. A `V.Sortable` item's rect leaves out the
translate and scale the drag gave it, as dnd-kit measures its droppables without their transforms; any other
droppable's includes its own translate.

A droppable on another panel of the tree is a candidate when both panels are screen-space runtime panels —
the main panel and a `V.Portal(layer:)` host, say — its rect mapped through screen space into the source's
panel, as a DOM page's one hit-testing surface reaches a portalled droppable. A droppable in a
`V.WorldSpace` panel is a candidate only for a drag on that same panel: the screen conversion Velvet maps
through does not hold for a world-space panel (`FiberCrossPanelPointerRouter` records the measurement), and
the DOM has no counterpart to it.

## Callbacks and state

`onDragStart`, `onDragEnd`, and `onDragCancel` run in the same discrete-input bracket as click
handlers: state written there commits synchronously in the same frame — a sortable list's
`onDragEnd` reorder is visible immediately, and wrapping rows in `V.Motion(layoutId:)` makes the
post-drop reorder animate with no extra wiring. `onDragMove` fires on every move, before the
`onDragOver` the same move causes, with the target the drag was over before it; `onDragOver` fires
only when the winning target CHANGES (including to null). Both stay on the normal frame-boundary lane.
Escape cancels a pointer drag; a cancelled (or torn-down) drag restores the source's inline translate and
every applied class by construction.

The `while*Class` channels cover styling with zero re-renders. For structural state, the hooks read the
nearest `V.DndContext`, as dnd-kit's hooks read their context; each one's XML documentation gives what
re-renders its component:

| Hook | dnd-kit | Returns |
| --- | --- | --- |
| `Hooks.UseDraggable(id)` | `useDraggable` | `DraggableState`: `IsDragging`, `Active`, `Over`, and `Transform`, the drag's translate while this draggable is the one dragged (re-renders on every move then) |
| `Hooks.UseDroppable(id)` | `useDroppable` | `DroppableState`: `IsOver`, `Active`, `Over` |
| `Hooks.UseDndContext()` | `useDndContext` | `DndContextState`: `Active`, `Over`, `Collisions`, `ActiveNodeRect` |
| `Hooks.UseDndMonitor(listener)` | `useDndMonitor` | nothing; each `DndMonitorListener` callback runs after the context's own callback of the same name |
| `Hooks.UseSortable(id)` | `useSortable` | `SortableState`: `Index`, `ActiveIndex`, `OverIndex`, `NewIndex`, `IsSorting`, `IsDragging`, `IsOver`, `Items`, `ContainerId` |

The hooks are read-only: the element still carries `V.Draggable`, `V.Droppable` or `V.Sortable` (or the
props slots), which do the dragging. `UseDndMonitor` throws outside a `V.DndContext`; the others return the
idle state there.

The `while*Class` channels apply their utilities as a payload does, so a bracket value and the utilities Velvet realises
itself (`shadow-*`, gradients, `skew-*`, `gap-*`, …) apply through them, ranked as
[styling-variants.md](styling-variants.md#payloads-velvet-realises-itself) gives: `whileDraggingClass` as a
gesture state, `whileOverClass` and `whileDragActiveClass` at `data-[…]:`'s rank.

## Accessibility

Each drag event is announced to a screen reader, dnd-kit's `announcements`: `accessibility:` takes a
`DndAccessibility` whose `Announcements` (a `DndAnnouncements` of one function per event, each returning the
text or null) default to dnd-kit's English sentences, `DndAnnouncements.Default`. The text goes to
`Announce` when one is given, and otherwise to Unity's
`UnityEngine.Accessibility.AssistiveSupport.notificationDispatcher` while `AssistiveSupport.isScreenReaderEnabled`
holds, where dnd-kit writes it into an ARIA live region. dnd-kit's `screenReaderInstructions` have no
counterpart: they are an `aria-describedby` on the draggable, and Velvet builds no accessibility hierarchy
for its elements to hang one on.

## Modifiers

`modifiers:` on `V.DndContext` is a list of `DndModifier` delegates applied in order to every drag's
translate, dnd-kit's `modifiers`: the result moves the source, the active rect collision detection reads
and the overlay, and is the `Delta` the callbacks report; the pointer collision detection reads is left
alone. A `V.DragOverlay`'s own `modifiers:` apply to its position after the scope's. `DndModifiers` carries
`@dnd-kit/modifiers`: `RestrictToVerticalAxis`, `RestrictToHorizontalAxis`, `RestrictToParentElement`,
`RestrictToWindowEdges` (the panel's root), `RestrictToFirstScrollableAncestor`, `SnapCenterToCursor` and
`CreateSnapModifier(gridSize)`.

## Auto-scroll

While a pointer drag is active, the `ScrollView`s around the drop target (else the source) scroll when the
pointer nears their edges, dnd-kit's `autoScroll`: outermost first, the first view whose edge zone —
`ThresholdX` / `ThresholdY` of its size, 20% by default — holds the pointer, on an axis it can still scroll
along and that the pointer has moved toward since the drag started, scrolls toward that edge at up to
`Acceleration` px (10) per `IntervalMs` (5), faster the deeper the pointer is in the zone. The source's
translate makes up what its own `ScrollView`s scrolled since the drag started, so it stays under the
pointer. `autoScroll: DndAutoScroll.Disabled` turns it off.

## The overlay's drop animation

When a drag ends or is cancelled, each `V.DragOverlay` that showed it moves to where its source settled,
dnd-kit's `dropAnimation`: `DndDropAnimation.Default` runs 250 ms with CSS `ease` timing and holds the
source at opacity 0 meanwhile, then hides the overlay. Each frame heads for the source's position as that
frame measures it, the source found again by its id, so an overlay dropped onto a reordered list lands on
the item's new slot. Through the animation the overlay keeps showing the last children it showed,
so the `activeId` recipe clearing them in `onDragEnd` does not empty it. An overlay already on its source
hides at once, and `dropAnimation: DndDropAnimation.None` turns the animation off. A positioner declared
through the raw `DragOverlay` props slot animates the same way but cannot keep children its owner removes.

## Sortable lists

`V.SortableContext(items:, strategy:)` and `V.Sortable(id:)` are dnd-kit's `SortableContext` and
`useSortable`. Each `V.Sortable` is one element that is both a draggable and a droppable under its id; while
a drag of one of a context's items is over another of them, the context moves the others aside by its
`DndSortingStrategy`, writing an inline translate (and scale) with a 200 ms `ease` transition, and puts every
value back when the drag ends. The reorder itself stays app logic, as in dnd-kit: `onDragEnd` reads
`Active.Id` and `Over?.Id` and commits `DndSortable.ArrayMove(items, from, to)` (or `ArraySwap`).

| Strategy | dnd-kit |
| --- | --- |
| `DndSortingStrategies.Rect` (the default) | `rectSortingStrategy` |
| `DndSortingStrategies.VerticalList` | `verticalListSortingStrategy` |
| `DndSortingStrategies.HorizontalList` | `horizontalListSortingStrategy` |
| `DndSortingStrategies.RectSwapping` | `rectSwappingStrategy` |

The dragged item follows the pointer itself under `movement: DragMovement.Translate`; under
`DragMovement.None` (the `V.DragOverlay` pattern) the strategy moves it to the slot it would drop into
instead, as dnd-kit does when a `DragOverlay` shows the drag. `DraggableInfo.Sortable` and
`DroppableInfo.Sortable` carry an item's `ContainerId`, `Index` and `Items`, dnd-kit's `data.sortable`, and
`DndSortable.KeyboardCoordinates` is `sortableKeyboardCoordinates`, for a `DndKeyboardSensor` that steps
from item to item.

## Deviations

- **Multi-touch / concurrent drags** — one active drag per mounted tree (dnd-kit's `active` is
  singular too); extra pointer-downs during a session do not arm.
- **A `V.WorldSpace` droppable** — reachable only from a drag on its own panel; see
  [Collision detection](#collision-detection).
- **A scaled ancestor** — the translate is panel-space, so a scaled ancestor skews tracking (a caveat
  dnd-kit shares).

A draggable inside an engine `ListView` with `reorderable: true`, or inside a touch `ScrollView`, keeps its
press: every move a pending or active drag observes carries UI Toolkit's draggable claim
(`PointerMoveEvent.isHandledByDraggable`), which the `ListView`'s drag processor and the `ScrollView`'s touch
scrolling check before starting a gesture of their own.
