### Changed

- A `V.Motion` with no `animate` of its own plays a mount enter from the labels it inherits, as a Framer
  Motion variant child does: it takes the nearest ancestor Motion's `initial` label along with its `animate`
  one, mounts at its own pose for that `initial` and enters to its pose for the `animate`. Children mounting
  with an entering parent enter in the slots the parent's `StaggerChildrenSec`, `DelayChildrenSec` and
  `When = BeforeChildren` give them, a presence child's included; one mounting under a parent already
  mounted enters on its own. Such a Motion used to mount at rest, and one naming its own `initial` under an
  inherited `animate` logged a warning instead of entering.

- `StaggerChildrenSec` numbers each Motion's own inheriting children, as Framer's `staggerChildren` does. An
  inheriting child with variants starts its own inheriting children with itself instead of handing them the
  next slots of its parent's sequence, and every inheriting child with variants takes a slot whether or not
  the label changes its pose — on an exit, every one that names no `exit` of its own. Label changes,
  mount enters and presence exits all number this way.

- An `initial` pose applying no class starts the mount enter from the Motion's own classes, as a Framer
  `initial` naming no value starts from each value's current one. It used to cancel the enter.
