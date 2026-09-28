### Fixed

- A `V.AnimatePresence` child `V.Motion` that comes back in the render that changes its label rests at the new
  pose's `translate-*` value. Its own enter cancelled the pose swap that would have written the value, so the
  element kept the old translate beside the new pose's class. This held for a child following its parent's label
  whose `transition` is a timed classic one such as `StyleTransition.Fade`, or a spring or a bezier, and for a
  child with an `initial` pose whose translate matches the new label's.

- A presence child `V.Motion` whose `transition` is a spring or a bezier, and which follows its parent's label,
  no longer cuts short the pose swap its label change starts in the render that adds it back. That swap now
  tweens on the pose's transition, as it does for `StyleTransitionConfig.None`.

- A `V.Motion` label change into a pose whose transition has zero duration, such as `StyleTransitionConfig.None`,
  lands the properties that pose names within two frames. A tween swap that had already swapped went on tweening
  them on its own transition, and a spring or bezier play or reversal still running kept driving them toward its
  own target until it settled, where a `translate-*` value went back to the earlier pose's. Properties the pose
  does not name keep moving as before.
