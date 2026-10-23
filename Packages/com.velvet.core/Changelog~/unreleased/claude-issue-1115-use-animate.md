### Added

- `Hooks.UseAnimate()`, Framer Motion's `useAnimate`: a `scope` ref and an `Animator` that animates opacity, x, y, scale
  and rotation keyframes of elements inside the scope by reference or selector, alone or as a sequence whose segments
  overlap through `AnimateAt` (`Time`, `WithPrevious`, `Offset`), returning `AnimateControls` with `Play`, `Pause`,
  `Speed`, `TimeSec`, `Then`, `Finished`, `Stop` and `Cancel`.
