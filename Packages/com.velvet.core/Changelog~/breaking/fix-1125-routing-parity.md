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
  router, and so do `V.Link`, `V.NavLink` and `V.Navigate`. They used to answer with a null location or
  empty data, and to navigate the most recently constructed router, if any. `V.Outlet`, `Hooks.UseParams` and
  `Hooks.UseOutletContext` still answer there, as React Router's do. A tree that publishes the routing
  contexts by hand publishes `RouterContext.Router`, which is new, beside them.

- A `V.RouterProvider` rendered beneath another throws `InvalidOperationException`, as React Router
  refuses a `<Router>` inside another. It used to render, with the Outlets beneath it reading the route
  depth the outer router's Outlets had published.

- Navigation blocking follows React Router's `useBlocker`. `Hooks.UseBlocker` takes a `bool` or a
  predicate over `BlockerFunctionArgs` (`CurrentLocation`, `NextLocation`, `HistoryAction`), which
  replaces the NavigationAttempt type; the asynchronous predicate overloads of `Hooks.UseBlocker` and
  `RouteBlockerManager.Register` are gone. `RouteBlockerState.Location` replaces `Attempt`, and
  `Proceed` and `Reset` are delegates, null unless the Blocker is `Blocked`. A kept `Proceed` throws
  unless the Blocker is `Blocked`, and run while the Blocker holds a newer block it releases the
  navigation it was handed out for; a kept `Reset` returns the Blocker to `Idle` whatever it holds. The
  component calling `UseBlocker` re-renders when its Blocker's state changes.

- A router consults only the Blocker registered last, and warns each time it does so with more than one
  registered. Every registered Blocker used to be consulted.

- The Blocker is consulted before the path is matched and before Guards run, so a path no route matches
  is put to it, a Guard is not asked about an attempt it stopped, a Guard's redirect is not put to it,
  and a blocked attempt no longer cancels the navigation already in flight. A block stands until a
  navigation commits rather than until the next attempt reaches the Blocker, and disposing a Blocker's
  registration returns its state to `Idle`. The ResetAllBlocked method of `RouteBlockerManager` is gone.

- Stepping `GoBack` or `GoForward` runs the destination route's loaders as a push to it does, since React
  Router keeps no loader data per history entry. It used to serve the data and errors the entry's
  loaders had produced when it was last shown, without running them.
