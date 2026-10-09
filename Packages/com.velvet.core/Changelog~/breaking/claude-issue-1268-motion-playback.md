### Changed

- `AnimationSequenceControls.Pause` holds the `Spring` and `Bezier` plays a sequence's steps started, where it
  froze only the cursor and let them run on; `Play` resumes them. A play is the sequence's when its `V.Motion`
  is handed `AnimationSequenceState.CurrentTransition`, or takes its label from one that is, the mount enter
  of a child that mounts there included. A play a step starts while the sequence is paused, such as step 0's
  under `autoplay: false`, now holds at its start until `Play`. A `Tween` play still runs on to its end.
- `AnimationSequenceState.CurrentTransition` is a copy of the step's transition, equal to it in every setting,
  rather than the instance the step was given: a comparison by reference against the step's config no longer
  holds.
- A `Spring` play on a sequence is sampled at its time rather than stepped frame by frame, so a frame longer
  than 1/30 s no longer slows it, and a heavily overdamped spring reaches the cap Framer Motion's spring
  generator puts on its hyperbolic argument (300) and lands on its target there, as Framer's does, where
  stepping never reached it.
- After `Cancel`, `Play` or `Restart` starts each new `Spring` or `Bezier` play from the values the cancel left
  on the channels its classes name, rather than from its pose's own, and an exit starts from them too; the held
  values no new play takes over come off once the reseed commits.
