### Fixed

- A `V.Portal` whose patch removed a `V.Portal` nested in it on the same target recorded too few of the rows it renders
  as its own, and left the rest on the target when it unmounted. It now removes every row it renders, as React's portal
  removes the host children it holds.
