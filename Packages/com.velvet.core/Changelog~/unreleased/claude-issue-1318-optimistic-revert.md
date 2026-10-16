### Fixed

- `Hooks.UseOptimistic` takes a settled transition's entries down in the commit that lands the transition's
  last work, in every component holding them. A holder the landing drain does not render, and one its pass
  rendered before a descendant it subsumes settled the transition, used to keep showing the entries until a
  follow-up render after that commit, so a layout effect of the commit read the holder and the components
  reading its value with the entries still in place. Each is now rendered without them before the drain's
  layout effects run. A settle that no flush lands, an `async` action completing, still leaves the render to
  the scheduler. The migration guide's `useOptimistic` row no longer lists this as a deviation.
