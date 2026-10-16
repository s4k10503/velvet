### Changed

- A node a component keeps in `Hooks.UseState`, the element-in-state pattern, stays out of the VNode pool while a
  queued write carries it or the state it was skipped from is that node, as it does while the state itself
  holds it. A write that a render skips is replayed by a later render, which emits the node it carries.
