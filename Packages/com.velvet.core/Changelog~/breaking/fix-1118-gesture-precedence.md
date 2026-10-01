### Changed

- `whileHoverClass`, `whileTapClass` and `whileFocusClass` apply their classes as `hover:`, `active:` and
  `focus:` apply a payload: while the state is on, one outranks the element's own utility for the same
  property and the base comes back when it goes off, a tap class outranks a hover one, and a bracket value
  such as `bg-[#f00]` applies. A gesture class used to rank with the element's own classes, so a base class
  the stylesheet declares later, a base bracket value or a divider's color kept the property, and a bracket
  value did nothing.
