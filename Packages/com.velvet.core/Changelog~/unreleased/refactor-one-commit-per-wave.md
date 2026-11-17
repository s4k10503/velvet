### Fixed

- A component a transition's last work owes a render, to take its `UseOptimistic` entries down, now commits in the
  same commit as that work where the work commits, so the refs that work replaces detach before that component's
  layout effects run.
- An error a new element's panel-attach callback throws as a batch of updates, a flush, a mount or a resumed time-sliced
  render applies the writes its renders recorded now goes, in a follow-up commit, to a boundary searched for from where
  React's commit starts the search for that element's placement, so a boundary below the component whose update was
  rendering can catch it. Before, it was handled as a render error of the component whose update was rendering. Where
  the element is off the panel again once those writes have applied, the error is not reported.
