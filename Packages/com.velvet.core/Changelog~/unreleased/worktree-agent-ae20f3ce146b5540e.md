### Fixed

- The end of a pass no longer looks through every child of each parent whose children it reconciled for one that
  carries an `absolute`, `relative`, `z-*` or `order-*` class (behind a variant too), is a `V.Anchored` element or
  is a pinned `AnimatePresenceMode.PopLayout` exit. It looks only under a parent such a child has stood in since a
  look there last found none. A time-sliced render looked after every slice, which added a cost growing with the
  square of the list's length.
- An element an `AnimatePresenceMode.PopLayout` exit pinned out of flow gives the pin up when it is removed, so a
  pooled `V.Label` or `V.Button` no longer brings one into its next mount.
- An element wrapped for a `clip-path-*` class or by a `wrapElement`, which a time-sliced pass created and a fresh
  pass discarded before placing it, no longer stays in the reconciler's stacking tables where its stacking classes
  left it in its slot, such as `relative`, `absolute`, `z-0`, `z-auto`, `order-0` or `order-none` with no non-zero z
  or order beside them, or a `z-*` or `order-*` reading a custom property that had not resolved.
