### Changed

- `Hooks.UseOptimistic` ties each optimistic entry to the transition whose `startTransition` callback added
  it, and discards the entry when that transition settles, or when no `async` action is left in flight (below): whether or not the pass-through
  state changed, and whether the action succeeded or faulted. An entry no render has folded yet when its
  transition settles is disowned instead, so the render `addOptimistic` requested still shows it, and the
  component's next Transition-lane render discards it. An entry added outside every
  `startTransition` callback while an `async` action is in flight, that action's own code after an `await`
  included, belongs to the actions in flight together and is discarded once none is left. As React entangles
  its async actions, a transition that settles while any is in flight hands its entries to them as well, and an
  action counts from its start until its task completes, whether or not the component that started it is
  still mounted. An action awaiting a task that never completes therefore holds every later transition's
  entries, its component's unmount included, until the next Play Mode entry; earlier an unmount gave the
  action up. The
  Transition-lane render that lands a transition's last work leaves that transition's entries out, so the
  commit already shows them gone. Each render folds
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
