### Changed

- `Hooks.UseOptimistic` ties each optimistic entry to the transition whose `startTransition` callback added
  it, and discards the entry when that transition's `isPending` clears: whether or not the pass-through
  state changed, and whether the action succeeded or faulted. An entry added outside every
  `startTransition` callback while an `async` action is in flight, that action's own code after an `await`
  included, belongs to the actions in flight together and is discarded once none is left. Each render folds
  the pass-through state through the entries still outstanding, so an entry whose transition is still
  pending is applied over a pass-through state that changed meanwhile instead of being dropped by that
  change, and one transition settling leaves another's entries in place. An entry nothing owns is shown by
  the render `addOptimistic` requests and discarded at the component's next Transition-lane render; added
  outside every `startTransition` callback with no `async` action in flight, it also logs a warning in the
  editor. The migration guide's `useOptimistic` row lists where this differs from React. The override used
  to last until the pass-through state changed, so an action that settled without changing it left the
  optimistic value on screen until something else did. A caller that called `addOptimistic` outside any
  transition, around a `UseMutation` call for instance, calls it inside the `startTransition` callback that
  performs the update, or after an `await` inside that callback's `async` action.
