### Changed

- A component whose body a hook or a call used to keep out of the automatic memoization is now woven.
  A call ahead of the last hook that may run a hook the weaver cannot see (an interface or virtual dispatch, or a
  method of an assembly the post-processor cannot read) no longer leaves the component unwoven: the gate declines
  to hit on a render in which a hook ran inside it, and a scope that reads none of what the hook changed still hits. `Use` and `UseFallback` are on the allow-lists, and a component
  calling `UseMutation`, `UseBlocker`, or a hook the weaver has not classified is woven with a gate that never hits,
  so that the scopes not reading what the hook returns are still cached. A component that takes a `UseMutation`
  result and keeps its UI in step with it needs no change.
  A scope no longer caches what it builds from a list or object that a call outside every scope is given, such as
  a `Grow(items)` statement in the body, wherever it stands, or a copy of the variable passed to one: it is built on every render the gate misses, as before.
  A component calling a custom hook that composes such a call is woven with a gate that never hits.

### Added

- `VELVET_REPORT_AUTOMEMO`, an assembly scripting define symbol that makes the post-processor emit one warning per
  `[Component]` saying why it is left unwoven or what was woven into it.
- `Hooks.BeginOpaqueCall` and `Hooks.EndOpaqueCall`, the bracket the compiler transform weaves around a call it
  cannot prove runs no hook. They are not for calling by hand.
