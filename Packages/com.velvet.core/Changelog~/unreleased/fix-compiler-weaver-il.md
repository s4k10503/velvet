### Fixed

- A `[Component]` calling a hook inside another call's arguments, such as
  `=> V.Label(text: Hooks.UseStore(store, v => v).ToString())`, no longer throws an InvalidProgramException on
  its first render. Auto-memoization placed its cache check right after the hook, where the arguments evaluated
  ahead of it were still on the evaluation stack, and returned the cached tree over them. The check now
  discards them first, so such a component keeps its auto-memoization.
