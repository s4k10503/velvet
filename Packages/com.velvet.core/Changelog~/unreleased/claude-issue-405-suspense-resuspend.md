### Fixed

- An exception that leaves a next-frame update batch, such as one a `Hooks.UseImperativeHandle` factory throws
  in that batch's layout commit, no longer stops the tree from scheduling later next-frame batches. A later
  Normal-priority update used to stay uncommitted until something flushed the batch synchronously, such as the
  end of a discrete event handler. An update queued in the batch before the exception left it now commits on
  the next frame. After three batches in a row have thrown, the updates still queued are dropped with an error
  log rather than retried on the next frame.
- A Transition update waiting for the callback that ran such a batch is rescheduled for a later frame, so its
  `isPending` no longer stays true with nothing left to commit it.
