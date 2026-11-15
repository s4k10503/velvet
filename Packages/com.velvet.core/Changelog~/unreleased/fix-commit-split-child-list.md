### Fixed

- A reconcile that throws partway no longer leaves in the container it reconciles into the rows it had inserted or
  moved before the throw. The container keeps the order the next render diffs from.
