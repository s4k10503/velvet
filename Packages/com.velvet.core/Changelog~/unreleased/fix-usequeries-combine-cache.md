### Fixed

- At the first render after a mount over one query or more, `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` ran
  their `combine` again where no query's result, no key and not the function itself had changed. That render now
  returns the value the mount's render combined, as TanStack Query v5's `useQueries` does.
- When a component's first render suspended after `Hooks.UseQueries` or `Hooks.UseSuspenseQueries` had run its
  `combine`, and a query's data changed before the render was retried, the retry returned the earlier value and showed
  it until the component rendered again. The retry now returns what `combine` makes of the data it reads.
