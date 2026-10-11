### Changed

- A navigation runs only the loaders React Router's default `shouldRevalidate` would: a route at the same
  place in the committed chain, over the same pathname as the URL spells it, keeps its data and its
  loader's token, so a parent layout's loader no longer runs again when only its child changes. No route keeps
  its data when the search changes, when the URL is the one already committed, or on the first navigation
  to commit after a route action started, and a route with no settled data keeps none either. Every
  matched loader used to run on every push, Back and Forward.

- `RouteMatch.PathnameBase` spells a literal segment as the URL does, as React Router's `pathnameBase`
  does, so a relative navigation from a route matched case-insensitively keeps the URL's case. It used to
  take the route pattern's spelling.

- `NavigationLifecycle` has a `Submitting` member, reported while a submission's guards and action run.
<!-- corrects: - `NavigationLifecycle` and `RouterStatus` each have a `Submitting` member, reported while a submission's -->

- A `Proceeding` Blocker stays `Proceeding` when the navigation it released is blocked by a Blocker
  registered after it, as React Router's does. It used to return to `Idle`.

- `Router.Current` falls back, when the router it names is disposed, to the most recently constructed
  earlier router that is neither disposed nor unreferenced. It used to become null, even with an earlier
  router still alive.
