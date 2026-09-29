### Changed

- A `V.Motion` naming none of `animate`, `initial` and `exit` plays a mount enter from the labels it
  inherits, as a Framer Motion variant child does: it takes the nearest ancestor Motion's `initial` label
  along with its `animate` one, mounts at its own pose for that `initial` and enters to its pose for the
  `animate`. Children mounting with an entering parent enter in the slots the parent's `StaggerChildrenSec`,
  `DelayChildrenSec` and `When = BeforeChildren` give them, a `V.AnimatePresence`'s keyed children
  included, so a presence listing inheriting Motions under a staggering parent staggers them in. One
  mounting under a parent already mounted enters on its own. Such a Motion used to mount at rest.

- A `V.Motion` naming any of `animate`, `initial` and `exit` inherits no label, as Framer treats a Motion
  naming a variant label as controlling its own. One naming only `exit` no longer follows its ancestor's
  `animate` label, and one naming only `initial` rests at that pose. The latter used to rest at the
  inherited label's pose, or at its own classes, and log a warning.

- `StaggerChildrenSec` numbers each Motion's own inheriting children, as Framer's `staggerChildren` does. An
  inheriting child with variants starts its own inheriting children with itself instead of handing them the
  next slots of its parent's sequence, and every inheriting child with variants takes a slot whether or not
  the label changes its pose. Label changes, mount enters and presence exits all number this way.

- An `initial` pose applying no class starts the mount enter from the Motion's own classes, as a Framer
  `initial` naming no value starts from each value's current one. It used to cancel the enter.

- `V.AnimatePresence(initial: false)` withholds the mount enter of every Motion that mounts under a child
  its first render created, for as long as that child stays, a later render's and a `V.Portal`'s included,
  as Framer's `PresenceChild` does; an inner presence's children answer to that presence. It used to reach only the Motions that first render created outside a
  portal.
