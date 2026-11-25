### Fixed

- An exception a `DetachFromPanelEvent` callback threw as an update or a boundary's swap to its fallback removed an
  element, a `z-*` element, one inside a `V.WorldSpace` or a row of a `V.VirtualList` included, was routed from the
  component whose render made the removal rather than from the component whose output held the element. So a
  boundary between the two was passed over, and a boundary whose swap to its fallback removed the children it showed,
  or that took down its own fallback to mount it again for a later error, sent the exception above itself, and with
  no boundary there the root unmounted the whole tree; where the swap was for an error a `UseFrame` callback threw,
  the exception instead stopped the boundary from showing its fallback and went above it in place of the callback's
  error. One a `V.VirtualList` row threw as a scroll took it out was thrown out of the scroll. An exception a
  `refCallback:` cleanup threw as its element was removed went above the boundary that rendered the element wherever
  that boundary had caught: in its swap, or as it took down its own fallback to mount it again. Each now goes to the
  nearest boundary at or above the component whose output held the element, a list row's being the component that
  rendered the list, passing over the components removed with it, as React captures what a deletion throws from the
  nearest mounted ancestor of the deleted child; a boundary whose swap took its children down reports it after the
  error it caught and keeps showing its fallback. Where the swap was for an error a `UseFrame` callback or a child's
  own render threw, or one the boundary's own render caught, and that fallback then throws or cannot be created, the
  boundary that replaces it reports it after the fallback's error, and an exception the effect cleanup of a
  `V.VirtualList` component row threw as the swap took the row down, which was dropped, is reported there as well.
  Where no boundary is above, the same exception thrown as the root renders nothing for an uncaught error was logged
  with `Debug.LogException` and never reached `MountOptions.OnUncaughtError`; it now reaches it after the error the
  root rendered nothing for.
