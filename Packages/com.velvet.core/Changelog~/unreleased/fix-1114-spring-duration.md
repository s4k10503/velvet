### Added

- `StyleTransitionConfig.Bounce`: with `DurationSec`, it describes a `Spring` that sets none of `Stiffness`,
  `Damping` and `Mass`, as Framer Motion's spring `bounce` does.

### Fixed

- A `Spring` label change that interrupts a running spring enter starts each channel the two share from the
  value it is drawn at, as Framer Motion animates every value from the one it has. A numeric channel keeps its
  velocity where the incoming spring is one its physics knobs describe; one its `DurationSec` describes, and
  a color, start with none, as in Framer. It used to jump to the pose the interrupted swap was heading for and
  spring from there at rest. A `Bezier` label change interrupting a running bezier enter likewise continues
  from the values drawn instead of jumping.
