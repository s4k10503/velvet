### Added

- `transition-discrete` and `transition-normal`, CSS's `transition-behavior`. `transition-discrete` leaves UI
  Toolkit's transition of a property it can only switch running, so the property switches to the new value partway
  through, as CSS switches a discrete property, and `transition-normal` takes that back; of the two, the one that ranks
  later wins, a variant's over the element's own.

### Fixed

- On an element Velvet renders, UI Toolkit's transition of a property it can only switch from one value to the other,
  such as `flex-direction`, `justify-content`, `position` or `background-image`, is cancelled at the panel's next
  animation update, which lands the new value; it switched the property partway through the transition, where CSS's
  default `transition-behavior` changes it at once. That holds whatever gave the element the transition:
  `transition-all`, a `duration-*` alone, a stylesheet's own `transition-property`, an inline write or a `V.Motion`
  `Tween` variant swap UI Toolkit's own transition runs. The font properties keep switching partway through.
