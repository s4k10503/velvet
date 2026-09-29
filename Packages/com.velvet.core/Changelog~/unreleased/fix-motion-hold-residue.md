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
  own target until it settled, where a `translate-*` value went back to the earlier pose's. A `filter` value
  lands the same way, and so does a value the tween was already headed for, where the pose spells it the way the
  tween's target does or as the same arbitrary value (`translate-x-4` for `translate-x-[16px]`); a stylesheet
  class and an arbitrary spelling of one value, such as `opacity-100` and `opacity-[1]`, are not recognised as
  one, and that property finishes the tween. A property a variant layer such as `hover:opacity-[0.3]` holds shows
  that layer's value at once rather than after the play settles. Properties the pose does not name keep moving
  on the play's timing. A bezier transition with zero duration now counts as zero duration here: it stopped the
  running play and snapped every property. A `MotionNode` built without `V.Motion`, which carries no
  transition, lands a label change the same way.

- A `V.Motion` whose tween, spring or bezier play, or a presence child's spring exit reversal, is still running
  when a zero-duration pose lands no longer ends carrying that play's earlier pose classes beside those of the
  timed pose that follows. The next pose's swap cancelled the play, and the cancel put back the classes the play
  had been started toward.

- A presence child `V.Motion` brought back mid-exit at a new label, or a `V.Motion` whose label changes into a
  pose whose transition has zero duration, no longer gets the earlier pose's arbitrary values, such as
  `translate-x-[40px]`, back when a later spring or bezier pose that does not name them settles. Where a spring
  or bezier was still moving such a value, the Motion rests at the play's target until its next label change,
  which now takes it away as the earlier pose's; an `opacity-[0.8]` left that way used to outrank a later pose's
  `opacity-50` for good.
