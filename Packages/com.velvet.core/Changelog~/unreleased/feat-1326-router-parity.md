### Added

- `ErrorResponse`, React Router's error response: `Status`, `StatusText` and `Detail`, the exception a navigation to a
  path no route matches commits with status 404. `Hooks.UseRouteError` returns it to the `errorElement` that renders it.

- `Hooks.UseMatches`, React Router's `useMatches`: every route the current location matched, parent first, as a `UiMatch`
  with its `Id`, `Pathname`, `Params`, loader `Data` and `Handle`. `RouteDefinition.Handle` (and `V.Route(handle:)`)
  carries the data a route surfaces there.

- `RouteDefinition.Id` (and `V.Route(id:)`), React Router's route `id`: the key of the route's loader data, and what
  `Hooks.UseRouteLoaderData` takes.

- `Hooks.UseRouteLoaderData`, React Router's `useRouteLoaderData`: a matched route's loader data by id, from any component
  beneath the provider.

- `Hooks.UseResolvedPath` and `Hooks.UseHref`, React Router's `useResolvedPath` and `useHref`: a target resolved from the
  route the calling component renders in, as `Hooks.UseNavigate` resolves it, as a `ResolvedPath` (`Pathname`, `Search`,
  `Hash`) or as the joined string. They take `RelativeRoutingType`.

- `Hooks.UseNavigationType` and `Router.NavigationType`, React Router's `useNavigationType`: whether the committed
  location was reached by a `Push`, a `Replace` or a `Pop` (nothing committed yet, or a step along the history).

- `Hooks.UseInRouterContext`, React Router's `useInRouterContext`: true beneath a `V.RouterProvider`.
