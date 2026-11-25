### Fixed

- A `has-[.class]:` or `has-[:checked]:` element now matches its descendants as they stand after each of these
  changes, as `:has()` matches the tree after any change:
  - a descendant relocated by `z-*` or `order-*` that the element's own render created, which it did not match until
    a later render, and one that render removed, which it kept matching;
  - the children of a `V.Portal` that unmounted from a target inside the element, which it kept matching unless that
    render also rendered the element or something inside it;
  - a class a play writes onto a descendant, a `V.Motion` pose's or an enter or exit class, which it did not follow
    until a later render re-derived it;
  - the children a `V.Portal` of another root mounts inside the element, which it did not match;
  - the rows of another root mounted inside the element, which it did not match until a render of its own root
    re-derived it, and the same rows once that root is disposed, which it then kept matching.
- A child of an element that holds the rows of two roots, one root's element that another root's `V.Portal` renders
  into or that another root is mounted into, now matches its structural variants (`first:`, `last:`, `only:`…) by its
  place among all of the element's children after either root changes them. A change one root made re-derived only
  the children whose variants that root declared, so `only:` kept holding beside the other root's rows.
- The rows of a `V.VirtualList` now match their structural variants (`first:`, `last:`, `odd:`, `even:`, `nth-N:`…)
  by their place among the rows the list shows, the elements a row the renderer returns as a component or a
  `V.Provider` renders among them. Their match was not derived from the rows a range placed, so a row a scroll made the
  first did not match `first:`, and a component row's elements matched by their place in an anchor of their own, so
  a component row rendering one element matched `first:` wherever it stood and never matched `even:`.
- A `has-[.class]:` element no longer matches a class of the application's own that only a variant payload
  (`hover:foo`) puts on a descendant, which it could when it re-derived its match before the panel's next layout pass
  after such a payload.
- A `V.VirtualList` range that took a row holding a `V.Portal` away outside a render, which a scroll or a resize does
  once the list's items have emptied, left the target's other children matching their structural variants as though
  the Portal's children were still there, and a `has-[…]:` element above the target matching those children. Both
  now follow the target as the range leaves it.
