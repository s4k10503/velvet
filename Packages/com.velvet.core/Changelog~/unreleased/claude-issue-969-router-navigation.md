### Fixed

- A navigation whose commit throws leaves `Router.CurrentLoaderData` and `Router.CurrentLoaderErrors` on
  the location already committed, as React Router writes `loaderData` and `errors` only with the location
  they belong to. It used to replace them with the results of the loaders it ran for the location it
  never reached.

- When a Guard starts a navigation that takes over and then redirects to a path a route matches,
  `Hooks.UseNavigation` goes on reporting the navigation the Guard started. It used to name the
  redirect's destination, which was never going to commit, while the Guard's navigation ran on.
