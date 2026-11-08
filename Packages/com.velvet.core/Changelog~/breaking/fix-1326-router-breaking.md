### Added

- `RouteDefinition.Index` and `V.Route(index: true)` make an index route, as React Router's `index: true`
  does: it matches its parent's own path, scores above a route of the same path, and is the route a bare
  `?index` in a submission's query targets. A tree that gives an index route a child list, an empty one
  included, throws `ArgumentException`, and so does `V.Route` given both.
- `SubmitOptions.EncType` is React Router's `encType`. `application/json` passes the body to the action as
  JSON, parsing a string body; `text/plain` sends it as text; a `get` takes neither and commits "Unable to
  encode submission body". Any value other than those and the default sends the form as
  `multipart/form-data`.
- `RouteActionContext.Request` is React Router's `request`: a `RouteRequest` whose `FormData()`, `Json()` and
  `Text()` read the body once, in the encoding it was submitted with, beside its `Method`, `Url` and
  `ContentType`. `FormData` is the web's `FormData`, entries in the order they were appended.
- `NavigationState.FormEncType`, `NavigationState.Json` and `NavigationState.Text` report the submission in
  flight as React Router's `navigation` does.

- `RouteLoaderContext.Request` is React Router's loader `request`: a GET `RouteRequest` with no body whose
  `Url` is the location's path and query resolved against `http://localhost` and serialized as the URL
  constructor serializes it, as memory history's createURL makes it.
- `ErrorResponse.Internal`, React Router's `internal`, is true on the responses the router makes.
- `SearchParams.Set`, `Delete`, `Sort` and `ToString`, and `ISearchParams.Values`, as URLSearchParams has them.

- `RouterOptions`, taken by `new Router(routes, scopeFactory, options)`, holds React Router's
  `createMemoryRouter` `initialEntries` and `initialIndex`; `Router.Initialized` is its `state.initialized`;
  `RouteDefinition.HydrateFallbackElement` and `V.Route(hydrateFallbackElement:)` are its
  `hydrateFallbackElement`.

### Changed

- A router stands on a location from construction, as `createMemoryRouter` does: on `/` with no options, its
  history's one entry keyed `"default"`, so a first navigation pushes a second entry with a key of its own where it
  used to make the first. A starting path no route matches commits its 404 at once, and one whose routes have
  a loader, a guard or a `redirectTo` runs them at once without reporting a navigation, the routes rendering
  their hydrate fallback, or nothing, until the first commit.

- The router's committed errors are React Router's `state.errors`: a loader's, an action's, a Suspend
  loader's and a refusal are keyed in `Router.CurrentLoaderErrors` by the nearest route with an `errorElement`
  at or above the route that failed, or by the root, where they were keyed by the failing route; of two reaching
  one route the higher failing route's stays, and a failed action takes the place of the first loader error.
  The highest matched route an error is keyed by renders it, and `Hooks.UseRouteError` reads the error keyed
  by the route it is called in. Loader data kept from the location before is dropped below that route, and a
  failed action's own route holds no data when no loader fails.

- The router commits an `ErrorResponse` as React Router's `getInternalRouterError` makes one: 405
  "Method Not Allowed" for a submission whose method no form takes and for one to a route with no action,
  400 "Bad Request" for a body it cannot encode, where it committed an `InvalidOperationException`. Its
  `Detail` is `"Error: "` and the message, the 404's included.
- `ISearchParams` and `SearchParams` are URLSearchParams: entries keep the order they were added in, a
  repeated name included, `Keys` names every entry, `Size` replaces `Count` and counts entries, and enumeration
  yields the entries. A query is written as URLSearchParams writes itself, a space as `+`, by the
  `Hooks.UseSearchParams` setter too.
- A relative target's leading `..` counts only the matched routes that contribute a path, as React Router's
  `getResolveToMatches` does, and a `..` after the first segment drops a URL segment rather than a route.
- `V.Route` takes a route with neither `element` nor `redirectTo`, which renders its matched child route in
  its place.
- JavaScript's `String` of an object read as a plain one is `[object Object]` in a submission body, where the
  object's own `ToString()` was written.

- A route whose `Path` is `""` is no longer an index route: it matches its parent's path like any other path,
  with no index score and no `?index` targeting. Mark an index route with `index: true`.
- A route whose `Path` is null is a pathless route, matched only through a child or as an index route, and
  `V.Route` accepts a null `path`. A route whose `Path` is `""` and has children now matches its own path with
  an empty `V.Outlet`, where it was treated as pathless.
- Routes are ranked as React Router's `flattenRoutes`, `rankRouteBranches` and `computeScore` rank them: each
  segment of a joined path scores one beside its own value, a path with optional segments makes a branch for
  each way of keeping or dropping them in place of a bonus for each optional segment, and equal scores are
  ordered as React Router's comparator orders them under V8 14.0's sort, over branches listed each child's ahead of
  its parent's where declaration order listed the parent first. An absolute child path must start with its
  parents' joined path and is matched after it; one that does not makes the tree throw `ArgumentException`.
- Every submission reads its body as React Router does, a `post` included: the default encoding reads it
  through the URLSearchParams constructor into a `FormData`, so a body it cannot encode commits "Unable to
  encode submission body" whatever the method. A `get`'s query is written as URLSearchParams writes itself,
  a space as `+`.
- `NavigationState.FormMethod` is upper-case, as React Router 7 reports it, and `NavigationState.FormData` is
  a `FormData`, null for a JSON or `text/plain` submission.

### Removed

- `RouteActionContext.FormData`. Read the body through `RouteActionContext.Request`.
