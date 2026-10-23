### Added

- `FocusScopeSettings.Wrap` and `V.FocusScope`'s `wrap:`: an arrow or d-pad move past one end of a
  `singleTabStop` group lands on the other, as a React Aria radio group's arrows do.

- `PanelFocusOrder.Appended`: a host panel's elements follow the declaring tree's own panel in Tab order, and
  the hosts follow each other in the order each came to hold an appended portal.

### Fixed

- Tab across a `PanelFocusOrder.Chained` host's boundary hands focus to the other panel inside the dispatch of
  the key, as a document's single focus owner moves. Before, the landing took focus on its panel's next
  scheduler tick, and no element held focus until then.

- An element focused in a panel Velvet renders into and then moved to another panel by user code is dropped
  from the first panel's focus memory, as an element leaving a document takes no focus with it. Before, that
  panel losing focus blurred the element in its new panel, and the panel regaining focus focused it there.

- An element released while it was gaining focus, and never mounted again, is dropped from its panel's focus
  chain on the panel's next tick, with its focus pseudo-state. Before, it stayed there until the panel's next
  focus change, and its re-scrub callback stayed on the pooled instance.
