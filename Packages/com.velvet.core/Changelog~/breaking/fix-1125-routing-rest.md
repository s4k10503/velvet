### Changed

- A navigation runs only the loaders React Router's default `shouldRevalidate` would: a route at the same
  place in the committed chain, over the same pathname as the URL spells it, keeps its data and its
  loader's token, so a parent layout's loader no longer runs again when only its child changes. A loader
  still runs again when the search changes, when the URL is the one already committed, where its route
  holds no settled data, and on the first navigation to commit after a route action started. Every
  matched loader used to run on every push, Back and Forward.

- `RouteMatch.PathnameBase` spells a literal segment as the URL does, as React Router's `pathnameBase`
  does, so a relative navigation from a route matched case-insensitively keeps the URL's case. It used to
  take the route pattern's spelling.

- `NavigationLifecycle` and `RouterStatus` each have a `Submitting` member, reported while a submission's
  guards and action run.

- A `Proceeding` Blocker stays `Proceeding` when the navigation it released is blocked by a Blocker
  registered after it, as React Router's does. It used to return to `Idle`.

- `Router.Current` falls back, when the router it names is disposed, to the most recently constructed
  earlier router that is neither disposed nor unreferenced. It used to become null, even with an earlier
  router still alive.
