### Fixed

- A `[Component]` calling a hook inside another call's arguments, such as
  `=> V.Label(text: Hooks.UseStore(store, v => v).ToString())`, no longer throws an InvalidProgramException on
  its first render. Auto-memoization placed its cache check right after the hook, where the arguments evaluated
  ahead of it were still on the evaluation stack, and returned the cached tree over them. The check now
  discards them first, so such a component keeps its auto-memoization.

- A `[Component]` declaring two locals from `Hooks.UseStore(...).ToString()`, whose hook results the compiler
  stores into one local, re-renders when the first hook's value changes and the second's does not.
  Auto-memoization keyed its cache on that local twice, so it held only the second value, and the component
  showed its previous output.

- An auto-memoized component that builds elements ahead of its last hook no longer leaves their pooled parts
  counted as rented when a render reuses the cached tree. Those parts are released to the garbage collector
  instead of staying held by the pool for the rest of the session.
