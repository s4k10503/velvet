### Added

- `RouteErrorResponse`, React Router's `ErrorResponse`: an exception carrying an HTTP `Status`, its
  `StatusText` and `Data`. `error is RouteErrorResponse` is `isRouteErrorResponse(error)`, and a loader or an
  action may throw one. The default error element shows one as its status and reason phrase, with no stack
  trace, as React Router's does.

### Changed

- A navigation to a path no route matches commits it, as React Router's `handleNavigational404` does. It
  takes over from a navigation in flight, runs no guard, action or loader, and commits a single match — the
  only top-level route, else the first top-level route that is pathless or `/`, else a stand-in route with
  no element — whose error is a 404 `RouteErrorResponse` (`No route matches URL "<path>"`), so the nearest
  `errorElement` or the default one renders. That route keeps the data its loader last settled with, every
  other route's is dropped, and the action data is cleared. `NavigateAsync` still returns
  `NavigationResult.NotFound` and `Router.Status` reads `NotFound`. It used to commit nothing and leave a
  navigation in flight running; a guard redirect to such a path likewise commits into the slot of the
  navigation it redirects.

- The 405 a submission records for a method no form takes, or for a route with no action, is a
  `RouteErrorResponse` with status 405 and reason phrase `Method Not Allowed`, as React Router's is. It used
  to be an `InvalidOperationException`; the message is unchanged.
