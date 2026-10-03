### Fixed

- A `V.Motion` that takes a `layoutId` another holder still holds crossfades with it over the move, as
  Framer Motion's shared layout does: it fades in while the holders behind it are drawn over its box and fade
  out from the previous holder's opacity, taking no pointer while they are drawn. The fade is written over the
  opacity the element's classes, variants and drivers give it, which keeps running under it and past it. Alone
  under the id, the lead mixes its opacity from the previous holder's.

- A filter cleared from a `transition-all` element while a Motion's own animation has its transitions suspended
  is cleared at once. The check for a running filter transition read the suspension's `transition-property:
  none` as naming filter.
