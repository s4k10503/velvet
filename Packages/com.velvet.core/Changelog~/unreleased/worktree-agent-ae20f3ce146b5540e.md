### Fixed

- The end of a pass no longer looks through every child of each parent whose children it reconciled for one that
  carries an `absolute`, `relative`, `z-*` or `order-*` class (behind a variant too), is a `V.Anchored` element or
  is a pinned `AnimatePresenceMode.PopLayout` exit. It looks only under a parent such a child has stood in since a
  look there last found none. A time-sliced render looked after every slice, so its cost grew with the square of the
  list's length.
- An element an `AnimatePresenceMode.PopLayout` exit pinned out of flow gives the pin up when it is removed, so a
  pooled `V.Label` or `V.Button` no longer brings one into its next mount.
- A `clip-path-*` element carrying an `absolute`, `relative`, `z-*` or `order-*` class that a time-sliced pass
  created, and a fresh pass discarded before it was placed, no longer stays in the reconciler's stacking tables.
