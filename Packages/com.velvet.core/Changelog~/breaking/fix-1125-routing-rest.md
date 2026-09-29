### Changed

- A navigation runs only the loaders React Router's default `shouldRevalidate` would: a route at the same
  place in the committed chain, over the same pathname, keeps its data and its loader's token, so a
  parent layout's loader no longer runs again when only its child changes. Every loader still runs when
  the search changes, when the URL is the one already committed, after a route action, and where a route
  holds no settled data. Every matched loader used to run on every push, Back and Forward.

- `NavigationLifecycle` and `RouterStatus` each have a `Submitting` member, reported while a route action
  runs.

- A `Proceeding` Blocker stays `Proceeding` when the navigation it released is blocked by a Blocker
  registered after it, as React Router's does. It used to return to `Idle`.

- `Router.Current` falls back to the most recently constructed router that has not been disposed when the
  one it names is disposed. It used to become null, even with an earlier router still alive.
