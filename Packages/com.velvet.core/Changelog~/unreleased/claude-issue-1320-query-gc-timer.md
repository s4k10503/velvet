### Fixed

- A `QueryClient` entry no query reads is now removed once its `GcTime` has passed on the client's clock,
  without waiting for a query to subscribe, `InvalidateQueries` to run or `SetQueryData` to write, as
  TanStack Query v5's `scheduleGc` timer removes it: an idle application no longer keeps every inactive entry
  and its data. The clock is polled once a frame, so removal lands on the first frame at or after the
  deadline. A query reading the entry again before then keeps it and the time starts over when its last
  reader leaves, `TimeSpan.MaxValue` sets no timer, and an entry with a request in flight is kept until a
  whole `GcTime` after the request settles, as before, with the clock read every frame meanwhile.
