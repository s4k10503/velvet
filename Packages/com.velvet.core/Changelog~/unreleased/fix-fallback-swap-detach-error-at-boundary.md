### Fixed

- As an error boundary's swap to its fallback removed the children it showed, an exception the `DetachFromPanelEvent`
  callback of one of their elements threw, a `z-*` element's included, went to the boundary above it, and with none
  there the root unmounted the whole tree, where the swap followed a render error in the boundary's own update or in
  an update of a component below it, or an error a commit captured for the boundary; one a `refCallback:` cleanup
  threw as such a swap removed an element the boundary rendered did the same. Where the swap followed an error a
  `UseFrame` callback threw, the detach exception instead stopped the boundary from showing its fallback and went
  above it in place of the callback's error. Each now goes to that boundary, which reports it after the error it
  caught and keeps showing its fallback, as React captures what a deletion throws from the nearest mounted ancestor
  of the deleted child. Where no boundary is above, the same exception thrown as the root renders nothing for an
  uncaught error was logged with `Debug.LogException` and never reached `MountOptions.OnUncaughtError`; it now
  reaches it after the error the root rendered nothing for.
