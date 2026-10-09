### Added

- `AnimationSequenceStep.Await(taskFactory)` holds a `Hooks.UseAnimationSequence` sequence until the
  `VelvetTask` its factory returns settles, the `await` a Framer Motion `useAnimate` caller writes between two
  animations, so a sequence can wait on a server response, a tap or a dialogue advance rather than only on a
  clock. The factory is handed a `CancellationToken` that a restart, a `deps` change or unmount cancels while
  the task is pending, and a task the sequence has left advances nothing. A task that settles while the sequence
  is paused is read once it resumes, and a fault reaches the nearest error boundary as a throwing `Call` step's
  does. The motion guide's Timelines section has the rest.
