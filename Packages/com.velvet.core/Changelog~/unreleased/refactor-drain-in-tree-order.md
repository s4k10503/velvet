### Fixed

- Components whose state is set in one batch now render in tree order, as React renders them, whichever was set
  first: an earlier sibling renders before a later one, and a `V.Portal`'s children render where the Portal sits. Before,
  components of the batch at one depth rendered in the order they were updated, and a deeper one rendered after every
  shallower one, wherever each sat in the tree.
- A component rendering nothing as the last of a `V.Portal`'s children now runs its insertion, layout and passive
  effects ahead of those of a later Portal's children, and a Portal's children now run theirs ahead of a component
  after the Portal that renders nothing as the last child of the element the Portal renders into, as React runs them.
  Before, either could run the other way round.
