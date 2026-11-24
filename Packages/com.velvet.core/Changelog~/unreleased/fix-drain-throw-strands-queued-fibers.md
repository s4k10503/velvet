### Fixed

- An exception that left a drain of queued updates as it rendered one component took the components queued behind that
  one off the queue without rendering them, and a later update of the same priority to one of them did not put it back.
  They now go back on the queue, so a later drain renders them with the updates they received since, as React keeps a
  root's updates pending until a render that takes them commits. Updates queued in a transition are kept the same way.
