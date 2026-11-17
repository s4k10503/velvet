### Fixed

- A component a transition's last work owes a render, to take its `UseOptimistic` entries down, now commits in the
  same commit as that work where the work commits, so the refs that work replaces detach before that component's
  layout effects run.
- An error a new element's panel-attach callback throws as a batch of updates, a flush, a mount or a resumed time-sliced
  render applies the writes its renders recorded now goes, in a follow-up commit, to the error boundary React's commit
  gives it: the nearest one at or above the component that rendered the element the write adds, passing over every
  component that mounts in the same commit, so that an error a `V.Mount` meets reaches the root. Where the element is
  off the panel again once those writes have applied, the error is not reported. Before, it was handled as a render
  error of the component whose update was rendering, so a boundary between the two never saw it.
