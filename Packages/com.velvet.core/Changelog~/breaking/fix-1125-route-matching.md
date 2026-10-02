### Added

- `RouteDefinition.Id` and `V.Route(id:)`, React Router's route `id`: the key a route's loader data, action
  data and error are held under. Two routes of one table sharing an id throw `ArgumentException` when the
  `Router` is built.

### Changed

- A route's `RouteMatch.RouteId`, the key `Router.GetLoaderData`, `Router.CurrentLoaderData`,
  `Router.CurrentLoaderErrors` and `Router.CurrentActionData` use, defaults to the route's position in the
  table — its index among its siblings joined to its parent's by `-`, as `"0"` or `"0-1"` — as React
  Router's does. It used to be built from the route's path, which two sibling pathless layouts, or an
  index route and a pathless layout beside it, shared.

- A pathless layout, a route with an empty path and children, matches only through one of its children,
  as React Router's does: a path that only its parent consumes matches the parent with an empty `V.Outlet`.
  It used to match on its own, scored as an index route, rendering the layout with nothing beneath it and
  running its loader. A route built with a null `Path` and no children no longer matches at all.
