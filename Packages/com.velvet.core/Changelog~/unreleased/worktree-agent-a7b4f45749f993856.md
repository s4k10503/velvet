### Fixed

- `Hooks.UseQueries` with a `combine` now re-renders with what the combine makes of a result that changed with
  no event from its entry, such as data turning stale as time passes, once the render that read it commits, as
  TanStack Query v5's `setQueries` does. It used to keep returning the earlier combined value until the
  component rendered again for another reason. Without a `combine`, such a commit re-renders the component as
  v5's does.
