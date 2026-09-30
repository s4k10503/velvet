### Changed

- `Hooks.UseAnimationSequence` takes its dependency list as a required second parameter, `deps`, ahead of
  `autoplay` and `loop`, and reads it as the React migration guide's dependency-list section describes:
  null restarts the sequence on every render and an empty array plays it once per mount. The list used to
  be an optional last parameter whose omission, or null, played the sequence once per mount. A caller that
  left it out, or passed null, passes `Array.Empty<object>()` to keep that behaviour.

- An error a `startTransition` callback throws, or its `async` action faults with, no longer reaches the
  caller: it is thrown from the declaring component's next Transition-lane render to the error boundary
  above it, as React's `startTransition` dispatches it as the `isPending` update, and a later call's outcome
  replaces an error not yet rendered. Once the declaring component has unmounted, the error is dropped. It
  used to propagate to the caller, or to be logged as an unobserved fault for an `async` action.

- Inside a `startTransition` callback, an `await` of a `VelvetTask` that had already completed suspends,
  as JavaScript's `await` resumes in a microtask once the code that called `startTransition` has returned:
  the continuation runs after a discrete handler returns, or on the main thread's next tick outside one, so
  the updates after it are no longer part of the transition and a handler's own writes after the call land
  first. They ran inline inside the transition. An `await` of a completed `Task`, `ValueTask` or
  `Awaitable` still continues inline, and what follows it stays in the transition.
