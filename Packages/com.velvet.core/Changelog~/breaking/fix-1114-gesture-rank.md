### Changed

- A `whileHoverClass` outranks a `whileFocusClass` where both are on and set the same property, and `whileTapClass`
  outranks both, ranking the gesture states as Framer Motion's `variantPriorityOrder` does. The focus class used to
  outrank the hover one.
