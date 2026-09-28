### Changed

- Under `transition-filter`, a filter change eases by the curve a USS transition takes for the same
  `ease-*` value, so it moves as it does under `transition-all`. The tween drew `ease`, `ease-in`,
  `ease-out` and `ease-in-out` as CSS's cubic-bezier keywords, where UI Toolkit eases `ease-in`,
  `ease-out` and `ease-in-out` quadratically, and drew every other easing mode linearly.
