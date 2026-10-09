### Fixed

- A navigation whose commit throws leaves `Router.CurrentLoaderData` and `Router.CurrentLoaderErrors` on
  the location already committed, as React Router writes `loaderData` and `errors` only with the location
  they belong to. It used to replace them with the results of the loaders it ran for the location it
  never reached.

- When a Guard starts a navigation that takes over and then redirects, the redirect ends with
  `NavigationResult.Cancelled`, as React Router ends a navigation that was aborted before its redirect
  is followed, and `Hooks.UseNavigation` goes on reporting the navigation the Guard started. A redirect
  to a path a route matches used to name its own destination there, which was never going to commit,
  and one to no route or past the redirect limit returned `NavigationResult.NotFound` or
  `NavigationResult.Error`.

- A takeover of a navigation parked on its loader goes straight from the one to the next, as React
  Router's navigation does. When the parked navigation's loader honoured its token, the router briefly
  reported `NavigationLifecycle.Idle` between them.

- A navigation started from a handler of a commit's own notifications, and that commits at once, is the
  last location `Router.OnLocationChanged` announces to every subscriber. The announcement it interrupted
  used to go on afterwards with the older location, to every subscriber when the handler was one of the
  commit's navigation-state ones and to the location subscribers after the handler when it was one of
  those, leaving `V.RouterProvider` on a location the router had left.
