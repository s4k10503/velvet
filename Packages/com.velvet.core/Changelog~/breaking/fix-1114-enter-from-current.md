### Changed

- A `V.Motion` with variants that resolves an `animate` label and no `initial` label, its own or inherited, plays a
  mount enter from the look it has without its variant classes to its `animate` pose, as Framer Motion animates a
  component naming no `initial` from the values it has. Children inheriting the labels of such a Motion enter in the
  same way, and a presence child added later does too. They used to mount at the `animate` pose with no enter.
  `V.Motion(enterOnMount: false)` mounts the Motion and the Motions below it at their resting pose with no enter, as
  Framer's `initial={false}` does, and so does `transition: StyleTransitionConfig.None` for one Motion.

### Added

- `V.Motion(enterOnMount:)` and `MotionNode.EnterOnMount`.
