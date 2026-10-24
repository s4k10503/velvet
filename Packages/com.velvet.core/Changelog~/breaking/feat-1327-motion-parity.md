### Changed

- A `TransitionType.Bezier` play with `BezierX1` or `BezierX2` outside `[0,1]` now plays that curve, as Motion's
  `cubicBezier` does. It fell back to the default curve with a one-shot warning before, so a play that relied on
  the fallback now runs a different curve. A non-finite coordinate still falls back.
