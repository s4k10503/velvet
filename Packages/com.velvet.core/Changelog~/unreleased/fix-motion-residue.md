### Fixed

- A descendant `V.Motion` of a removed `V.AnimatePresence` child — one that is not the child's anchor —
  moves to a `translate-*` `exit` pose at its exit's swap, as the anchor does. Its exit moved the pose's USS
  classes only, so it stayed at its resting translate until the child was removed.

- A descendant `V.Motion` of a `V.AnimatePresence` child whose key comes back rests at the pose the re-added
  child declares. Its exit put back the pose it had started from after the new one had been applied, so a
  descendant re-added at `opacity-50` from `opacity-100` carried both classes, whether its exit was still
  playing or had completed before the render that would have removed it.

- A `V.Motion` label change into a pose whose transition has zero duration, such as
  `StyleTransitionConfig.None`, lands on that pose alone when it follows a tween swap that has not swapped yet.
  The earlier swap still ran afterwards and put its own pose's classes back beside the new one's.
