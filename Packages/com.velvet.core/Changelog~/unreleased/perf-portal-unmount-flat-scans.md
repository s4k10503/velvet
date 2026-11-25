### Changed

- Unmounting many sibling Portals no longer costs the square of their count. Taking a removed Portal down read every
  other Portal placed and every row of the target the Portals share, and its removal rewrote the start of every range
  behind it on that target. A removal now finds the Portals inside what it takes down by walking that subtree, and
  reads only their rows, and a target keeps its ranges' starts against an offset of its own, so a removal writes
  whichever side of it holds fewer ranges. Unmounting many sibling Portals that each render a component among their
  children still reads every component rendered into that target for each Portal it removes.

### Fixed

- Removing an element that holds several sibling Portals takes their children off the targets in the order the
  Portals appear in the tree, as React removes them, so their `DetachFromPanelEvent` callbacks run in that order.
  Where the removal was applied with the rest of the update's writes, the children left in the order the Portals'
  records sat in an internal table: a Portal that mounted before a sibling ahead of it in the tree went first, and the
  order could run backwards once other Portals had unmounted.
