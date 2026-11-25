### Fixed

- An exception a `DetachFromPanelEvent` callback threw as an update or a boundary's swap to its fallback removed an
  element, a `z-*` element or one inside a `V.WorldSpace` included, was routed from the component whose render made
  the removal rather than from the component whose output held the element. So a boundary between the two was
  passed over, and a boundary whose swap to its fallback removed the children it showed, or that took down its own
  fallback to mount it again for a later error, sent the exception above itself, and with no boundary there the root
  unmounted the whole tree; where the
  swap was for an error a `UseFrame` callback threw, the exception instead stopped the boundary from showing its
  fallback and went above it in place of the callback's error. An exception a `refCallback:` cleanup threw as its
  element was removed went above the boundary that rendered the element wherever that boundary had caught: in its
  swap, or as it took down its own fallback to mount it again. Each now goes to the nearest boundary at or above the
  component whose
  output held the element, passing over the components removed with it, as React captures what a deletion throws
  from the nearest mounted ancestor of the deleted child; a boundary whose swap took its children down reports it
  after the error it caught and keeps showing its fallback. Where no boundary is above, the same exception thrown as
  the root renders nothing for an uncaught error was logged with `Debug.LogException` and never reached
  `MountOptions.OnUncaughtError`; it now reaches it after the error the root rendered nothing for.
