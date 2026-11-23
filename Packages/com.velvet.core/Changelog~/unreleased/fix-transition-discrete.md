### Added

- `transition-discrete` and `transition-normal`, CSS's `transition-behavior`. `transition-discrete` transitions the
  properties `transition-all` leaves out, each switching to the new value partway through as CSS switches a discrete
  property, and `transition-normal` takes them back out; of the two, the one that ranks later wins, a variant's over
  the element's own. Neither changes which other `transition-*` utility or stylesheet rule outranks
  `transition-all`.

### Fixed

- `transition-all` no longer transitions properties UI Toolkit can only switch from one value to the other, such as
  `flex-direction`, `justify-content`, `position` and `background-image`; `visibility`, `background-position` and the
  font properties stay in. Such a property switched partway through the transition; it now changes at once, as under
  CSS's default `transition-behavior`, whether a class or an inline write changes it.
- A `V.Motion` variant swap UI Toolkit's own transition runs, and a `PropertyOverrides` entry named `"all"`, now
  transition what `transition-all` does on the element, where they transitioned every animatable property and switched
  the ones UI Toolkit cannot interpolate partway through.
