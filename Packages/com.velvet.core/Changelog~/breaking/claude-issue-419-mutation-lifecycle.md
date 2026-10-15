### Changed

- A `Hooks.UseMutation` call whose component unmounts while it is in flight runs its callbacks, as TanStack
  Query v5's option callbacks outlive the observer, where it ran none. Neither writes the handle or
  re-renders the component.
