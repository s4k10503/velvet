### Changed

- `Router.Navigation` is React Router's `router.state.navigation`: the navigation in flight, from the
  moment its path has matched a route until it commits or gives up — its `NavigationLifecycle`, the
  location it is heading for, resolved against the route tree so it carries the destination's `Params`
  and `Matches`, and the submission it carries — and `Idle` with every other member null while none is.
  `Router.OnNavigationChanged` is raised as it changes, and `Hooks.UseNavigation` returns it. An attempt
  that is blocked or matches no route never appears there, and an attempt reports how it ended through
  the `NavigationResult` it returns or the exception it throws.
<!-- corrects: - `Router.PendingLocation` — the location an in-flight navigation is heading for, resolved against the -->

### Removed

- Router.Status, Router.OnStatusChanged and the RouterStatus enum. Status mixed
  the navigation in flight with how the last attempt ended, so an attempt that matched no route overwrote
  the Ready a committed location had left. Read `Router.Navigation` and `Router.OnNavigationChanged` for
  the navigation in flight, `Router.CurrentLocation` for where the router is, and what an attempt
  returns or throws for how it ended.
