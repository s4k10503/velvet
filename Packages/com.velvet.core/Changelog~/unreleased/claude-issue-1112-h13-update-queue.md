### Changed

- A node a component keeps in `Hooks.UseState`, the element-in-state pattern, stays out of the VNode pool while a
  queued write carries it, the result of an updater already computed against it is queued, or the state a skipped
  write is replayed over is that node, as it does while the state itself holds it. A write that a render skips is
  replayed by a later render, which emits the node it carries.
