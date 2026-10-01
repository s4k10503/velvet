### Fixed

- A `V.Motion` pose change played as a tween puts back, as it ends, the transition duration, delay and easing
  the element holds inline of its own — a `duration-[x]` utility's, or one its code writes to `style`, whether
  before the tween started or while it ran. It cleared all three, so the element's transitions fell back to its
  classes until something wrote them again.
