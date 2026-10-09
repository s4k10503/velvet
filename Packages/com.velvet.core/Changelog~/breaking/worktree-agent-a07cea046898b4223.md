### Changed

- `AnimationSequenceControls.Pause` holds the `Spring` and `Bezier` plays a sequence's steps started, where it
  froze only the cursor and let them run on; `Play` resumes them. A play is the sequence's when its `V.Motion`
  is handed `AnimationSequenceState.CurrentTransition`, or takes its label from one that is. A play a step
  starts while the sequence is paused, such as step 0's under `autoplay: false`, now holds at its start until
  `Play`. A `Tween` play still runs on to its end.
- `AnimationSequenceState.CurrentTransition` is a copy of the step's transition, equal to it in every setting,
  rather than the instance the step was given: a comparison by reference against the step's config no longer
  holds.
