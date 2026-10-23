### Changed

- `V.DndContext` returns a `ComponentNode` rather than an `ElementNode`: it is a component rendering the scope
  element, as dnd-kit's `DndContext` is, and it provides the drag state to the new hooks. It takes
  `onDragMove:`, `sensors:`, `modifiers:`, `autoScroll:` and `accessibility:` after `activation:`, so a
  call passing `className:` or a later argument by position passes it by name. `DndContextSettings` takes a
  further `OnDragMove` constructor argument.
- `V.DragOverlay` returns a `ComponentNode` rather than a `PortalNode`. In a mount on an editor panel it
  renders its positioner in place on that panel, where it shows the drag, rather than portalling it into the
  Game view's Overlay layer, where it stayed hidden and logged a warning.
- When a drag ends or is cancelled, a `V.DragOverlay` away from its source moves back onto it over 250 ms
  while the source is held at opacity 0, and keeps showing its last children meanwhile, as dnd-kit's default
  `dropAnimation` does; it used to hide at once. `dropAnimation: DndDropAnimation.None` hides it at once.
- A Space or Enter key reaching a draggable, or an element inside one, starts a keyboard drag under the
  default sensors, as dnd-kit's default `KeyboardSensor` does: the arrow keys move it 25 px, Space, Enter or
  Tab drop it and Escape cancels it. A scope that wants pointer drags only passes
  `sensors: new DndSensor[] { new DndPointerSensor() }`.
- A drag announces its start, target changes, drop and cancel to an enabled screen reader, as dnd-kit's
  default `announcements` do.
- While a pointer drag is active, a `ScrollView` around it scrolls when the pointer nears its edge, as
  dnd-kit's default `autoScroll` does; `autoScroll: DndAutoScroll.Disabled` turns it off. A dragged source
  inside a `ScrollView` that scrolls keeps under the pointer.
- A droppable on another screen-space runtime panel of the tree, such as one inside `V.Portal(layer:)`, is a
  collision candidate for a drag from the main panel, where only droppables on the source's own panel were.
- `DragOverArgs.Delta`, `DragEndArgs.Delta` and the source's translate are the drag's translate after the
  scope's modifiers.
- `com.velvet.core` depends on `com.unity.modules.accessibility`.

### Added

- `Hooks.UseDraggable`, `Hooks.UseDroppable`, `Hooks.UseDndContext` and `Hooks.UseDndMonitor`, dnd-kit's
  hooks: `DraggableState`, `DroppableState` and `DndContextState` re-render their component as the drag
  changes, and a `DndMonitorListener` hears every drag event. `onDragMove` and `DragMoveArgs` carry every move.
- Sensors: `DndSensor`, `DndSensorActivation` and `DndSensorSession` for a custom sensor, the built-in
  `DndPointerSensor` and `DndKeyboardSensor` (with `DndKeyboardCodes` and a `DndKeyboardCoordinateGetter`),
  and `DndSensors.Default`.
- `DndAccessibility` and `DndAnnouncements`, to replace the announcements or send them elsewhere.
- Modifiers: `DndModifier`, `DndModifierArgs`, and `DndModifiers.RestrictToVerticalAxis`,
  `RestrictToHorizontalAxis`, `RestrictToParentElement`, `RestrictToWindowEdges`,
  `RestrictToFirstScrollableAncestor`, `SnapCenterToCursor` and `CreateSnapModifier`, on a scope and on a
  `V.DragOverlay`.
- `DndAutoScroll` and `DndDropAnimation`.
- The sortable preset: `V.SortableContext`, `V.Sortable`, `Hooks.UseSortable`, the strategies in
  `DndSortingStrategies`, `DndSortable.ArrayMove`, `ArraySwap` and `KeyboardCoordinates`, and
  `DraggableInfo.Sortable` / `DroppableInfo.Sortable`.
- `DndCollisionQuery.HasPointer`, false for a drag no pointer drives; `DndCollisions.PointerWithin` collides
  with nothing then.
