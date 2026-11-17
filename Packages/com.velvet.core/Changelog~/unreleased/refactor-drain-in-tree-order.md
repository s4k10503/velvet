### Fixed

- Components updated in one batch now render in tree order, as React renders them, whichever was updated first: an
  earlier sibling renders before a later one, and a `V.Portal`'s children render where the Portal sits. Before,
  components of the batch at one depth rendered in the order they were updated, and a deeper one rendered after every
  shallower one, wherever each sat in the tree.
