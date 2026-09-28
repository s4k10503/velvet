### Changed

- `Hooks.UseNavigation().Location` is null while the navigation state is `Idle`, as React Router's
  `navigation.location` is `undefined` then. It used to carry the committed location, the value
  `Hooks.UseLocation()` returns; read that hook for it instead.

- A loader error that no route in the matched chain declares an `errorElement` for renders a default
  error element in place of the root route, as React Router's does: a heading, the exception's message
  and its stack trace, with the exception logged in the editor and in a development build. Nothing
  rendered there before.

- The routing hooks act on the router the nearest `V.RouterProvider` publishes, as React Router's act on
  the nearest `RouterProvider`'s: `Hooks.UseNavigate` (and `V.Link`, `V.NavLink`, `V.Navigate`),
  `Hooks.UseNavigation`, `Hooks.UseBlocker`, the `Hooks.UseSearchParams` setter, and the route scope
  factory an Outlet asks. They used to act on the most recently constructed router, so a tree under one
  provider read one router and navigated another. `Router.Current` stays, and constructing a second
  router no longer logs a warning: two live routers are each driven through a provider of their own.

- `Hooks.UseLocation`, `Hooks.UseNavigate`, `Hooks.UseMatch`, `Hooks.UseSearchParams`,
  `Hooks.UseNavigation`, `Hooks.UseBlocker`, `Hooks.UseLoaderData` and `Hooks.UseRouteError` throw
  `InvalidOperationException` beneath no `V.RouterProvider`, as React Router's refuse to run outside a
  router, and so do `V.Link`, `V.NavLink` and `V.Navigate`. They used to answer with a null location,
  empty data or a navigation that did nothing. `V.Outlet`, `Hooks.UseParams` and
  `Hooks.UseOutletContext` still answer there, as React Router's do. A tree that publishes the routing
  contexts by hand publishes `RouterContext.Router`, which is new, beside them.
