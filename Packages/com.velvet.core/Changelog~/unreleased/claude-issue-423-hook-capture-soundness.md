### Fixed

- Two custom hooks calling each other are each read as reaching every hook the pair reaches. One of them
  could be read as calling no hook, depending on which component the build met first, so a component
  calling it behind a condition, or calling it for a `UseMutation` the pair composes, was memoized where it
  should have been left unwoven.
- A component that deconstructs a custom hook's pair, `var (count, total) = Custom();`, is no
  longer auto-memoized on the first element alone, which served a stale tree after the second changed. A
  `ref var x = ref Custom();` over a custom hook leaves the component unwoven.
- A component the compiler weaver fails to process, because analyzing or weaving its body throws, is left
  unwoven and reported as a build warning naming it and the exception, where the failure used to break the
  assembly's compilation.
