### Changed

- A navigation to a path no route matches commits, as React Router's data router does. `Router.CurrentLocation` becomes
  that path, a history entry is added, `Router.CurrentLoaderData` empties, and the root route (the only top-level route,
  else the first with an empty path or `/`) carries an `ErrorResponse` with status 404 in `Router.CurrentLoaderErrors`,
  so its `errorElement` renders, or the default error element where it has none. No guard, action or loader runs for it.
  `Router.NavigateAsync` still answers `NavigationResult.NotFound`, now after committing; a guard redirect to a path no
  route matches commits the same way instead of ending without a commit. A caller that treated `NotFound` as "nothing
  changed" must read `Router.CurrentLocation` instead. A null path still commits nothing.

- `RouteMatch.RouteId` is unique per route. It is `RouteDefinition.Id` when the route sets one, and otherwise its
  position in the tree, the indexes from the top-level array down joined by `-` (`"0"`, `"0-1"`), where it was the
  route's cumulative path (`"/users"`, `"/room/?index"`) and collided for sibling pathless layouts and for an index
  under `/`. Loader data, action data and errors are keyed by it, so `Router.GetLoaderData`,
  `Router.CurrentLoaderErrors`, `RouterContext.LoaderData` and `RouterContext.Errors` lookups written with a path-shaped
  key take the new one, and the "did not provide an `action` for route" message names it. A tree whose routes share an
  id throws `ArgumentException`.

- A pathless layout, a route with an empty path and children, no longer matches as the leaf of its own path. It stays in
  the chain of every path a child matches; a path only the layout would have taken now matches the route beside it, or
  nothing, where it used to render the layout with an empty `Outlet`.
