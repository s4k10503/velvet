### Added

- `navigate(to, options)` takes `NavigateOptions`, as React Router's does: `Replace`, `State`, and `Relative`
  (`RelativeRoutingType.Route` or `.Path`, where `..` drops a segment of the calling route's pathname rather than a matched
  route). `PreventScrollReset` is accepted and read by nothing, as the router has no scroll restoration.
  `navigate(-1)` and `navigate(n)` move `n` entries along the history, and a delta past either end lands on that end
  entry; a delta that does not move runs the current entry's loaders again. `flushSync` and `viewTransition` have nothing to act on in UI Toolkit and are not offered.
  `Router.NavigateAsync(path, options)` and `Router.NavigateAsync(delta)` are the same calls without a component.

- `RouterLocation` gains `State`, `Key` and `Hash`, which `Hooks.UseLocation` returns. `Key` is `"default"` for the
  first entry and unique within the router for every later push or replace; a step along the history lands on the
  state and key its entry was created with.

- `V.Link`, `V.NavLink` and `V.Navigate` take `state` and `relative`. A target that is only a fragment, such as
  `#top`, keeps the current path and drops its query, and `V.NavLink` ignores the fragment when it decides whether
  it is active.
