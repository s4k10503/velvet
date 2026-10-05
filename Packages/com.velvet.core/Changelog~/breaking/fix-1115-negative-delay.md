### Changed

- A negative `StyleTransitionConfig.DelaySec` starts the animation that far into its run, as a CSS transition
  and a Framer Motion animation with a negative delay do. A tween writes it as its transition delay, in a
  per-property override as well, and a `Spring` or `Bezier` play starts partway and completes at once when
  the delay reaches past its end. A tween used to ignore it (except in a per-property override list that also
  held a positive delay), and a `Spring` or `Bezier` play started from the beginning. A
  `Hooks.UseAnimationSequence` `Spring` step's hold is shortened by a negative delay to match.
  Tween enter and exit plays combine orchestration offsets with their own and per-property delays,
  so a negative total advances the first animated frame rather than postponing the class swap.
