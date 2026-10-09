### Fixed

- Two custom hooks calling each other are each read as reaching every hook the pair reaches. One of them
  could be read as calling no hook, depending on which component the build met first, so a component
  calling it behind a condition, or calling it for a `UseMutation` the pair composes, was memoized where it
  should have been left unwoven.
- A component that deconstructs a custom hook's pair, `var (count, total) = UseCountAndTotal();`, is no
  longer auto-memoized on the first element alone, which served a stale tree after the second changed. A
  `ref var x = ref UseSlot();` over a custom hook leaves the component unwoven rather than producing a body
  the runtime rejects.
