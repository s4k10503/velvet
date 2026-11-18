### Fixed

- At the first render after a mount over one query or more, `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` ran
  their `combine` again where no query's result, no key and not the function itself had changed. That render now
  returns the value the mount's render combined, as TanStack Query v5's `useQueries` does.
