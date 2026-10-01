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

- A render that suspends with no boundary above leaves none of the props it passed to the component that
  suspended committed, as React discards its work in progress, and a component that render first mounted is
  rendered by the next render that reaches it: a later update renders the component again and suspends again
  while the resource is pending, where it skipped a memoized one and committed the rest around it.

- A component whose render suspends while another component's render reaches it below a `V.Suspense`, with
  no host element between it and the Suspense, keeps its state while the boundary shows its fallback, as
  React keeps it offscreen, and the boundary reveals it when its resource resolves. That render's pass
  disposed it, losing its state and the read the reveal waited on; one inside a host element of the
  boundary's children is still disposed with that element. A component the boundary had shown has its layout
  effects and imperative handles taken down in the commit that shows the fallback, none of the layout work of
  the render that hid it commits, and one that render first mounted runs none of its effects, passive ones
  included, and creates no handle until the reveal; on reveal the layout effects run again and the handles
  are created again, as React disconnects and reconnects them.

- A component that renders its own `V.Suspense` and whose read suspends below an outer boundary reveals
  through that boundary when the resource resolves, as React takes the nearest Suspense above the component
  that suspended. It committed its own rows while the outer boundary kept showing its fallback.
