### Changed

- `Hooks.UseOptimistic` ties each optimistic entry to the transition whose `startTransition` callback added
  it, and discards the entry when that transition's `isPending` clears, as React's `useOptimistic` does:
  whether or not the pass-through state changed, and whether the action succeeded or faulted. Each render
  folds the pass-through state through the entries still outstanding, so an entry whose transition is still
  pending is applied over a pass-through state that changed meanwhile instead of being dropped by that
  change, and one transition settling leaves another's entries in place. An entry added outside every
  transition logs a warning in the editor and is discarded at the component's next Transition-lane render.
  The override used to last until the pass-through state changed, so an action that settled without
  changing it left the optimistic value on screen until something else did. A caller that called
  `addOptimistic` outside `startTransition` calls it inside the callback that performs the update.
