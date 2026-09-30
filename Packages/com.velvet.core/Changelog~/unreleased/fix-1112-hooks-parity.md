### Added

- `Hooks.UseCallback` overloads named for `Action`, `Action` of two or three parameters and `Func` of one to
  three, so a C# 9 assembly, where a lambda has no type of its own, calls it without a type argument for a
  parameterless `Action` lambda or method group and for a lambda of those shapes with typed parameters, as
  TypeScript infers `useCallback`'s. None of them takes one type argument, so every explicit
  `UseCallback<T>(...)` call binds as it did.

### Fixed

- A `Hooks.Use` loader written without a `resourceKey` as a lambda rebuilt each render is kept through the
  StrictMode re-run of the render that read it, as React's second invocation keeps its thenable, where the
  re-run restarted it.

- A render that a component's own update starts no longer throws the Suspense signal out of the frame
  when something in it suspends, in any slice of a time-sliced render. The nearest boundary above renders
  again, and the updated component renders again inside it; with none, each render that suspended is
  retried when a resource of the component whose read suspended it resolves.
