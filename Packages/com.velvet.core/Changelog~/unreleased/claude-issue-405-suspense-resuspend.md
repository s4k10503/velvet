### Fixed

- An exception that leaves a next-frame update batch, such as one a `Hooks.UseImperativeHandle` factory throws
  in that batch's layout commit, no longer stops the tree from scheduling later batches. A later update outside
  a discrete input event used to stay uncommitted until something flushed the batch synchronously, such as the
  end of a discrete event handler. An update queued in the batch before the exception left it now commits on
  the next frame.
