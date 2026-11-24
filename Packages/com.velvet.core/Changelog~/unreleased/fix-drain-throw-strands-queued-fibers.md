### Fixed

- An exception that left a drain of queued updates as it rendered one component left the components queued behind that
  one unrendered and off the queue, and an update of the same priority made to one of them afterwards did not put it
  back. Each now renders at the next drain, with the updates it received since, as React keeps a root's updates pending
  until a commit renders them. Updates queued in a transition are kept the same way.
