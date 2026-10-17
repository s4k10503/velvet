### Changed

- `DndCollisionDetection` returns a ranked `DndCollision[]` (`Id`, `Data`, `Value`) instead of a single
  `string?`, as dnd-kit's `CollisionDetection` returns a ranked `Collision[]`. The first entry becomes the
  over target and an empty array means no collision. A custom strategy returns
  `new[] { new DndCollision(id, data, value) }` where it returned `id`, and `Array.Empty<DndCollision>()` where
  it returned `null`.
- The built-in strategies rank as dnd-kit's do. `DndCollisions.RectIntersection` ranks by the intersection
  ratio `intersection / (droppable + active - intersection)` rounded to four decimals, greatest first, where
  it ranked by the overlap area. `DndCollisions.PointerWithin` ranks the droppables containing the pointer,
  edges included, by the mean distance from the pointer to their four corners, smallest first, where it picked
  the smallest area, and a pointer on a droppable's right or bottom edge now collides. `DndCollisions.ClosestCenter` ranks by
  center distance. Equal values keep registration order. A drag whose active rect overlaps a large and a small
  droppable, or whose pointer sits in nested droppables, can now report a different `over` target.
- `DragOverArgs` and `DragEndArgs` take a further `Collisions` argument, the strategy's full ranking at the
  latest move, as dnd-kit's drag events carry `collisions`. Code constructing either record passes it; code
  reading the events needs no change.
