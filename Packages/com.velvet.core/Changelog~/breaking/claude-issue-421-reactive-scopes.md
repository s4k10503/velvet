### Changed

- A component's automatic memoization caches, below the component's own gate, each child `VNode` expression, each
  lambda that captures variables and each value the body derives and stores once, on the values it reads, as React
  Compiler's reactive scopes do, where it cached the component's tree as a whole and nothing below it. A render
  that changes one input builds again only what reads it and hands the reconciler the same `VNode` instances for
  the rest, which it skips. A constructor, a property accessor or a `System` member that a scope's expression
  calls no longer runs on a render where none of the values the scope reads changed.
  A scope compares the references it reads, so a list, dictionary or object held in state, context or props and
  changed in place counts as unchanged and the scope keeps what it built before: replace the value (a new
  list, a new object) as in React, or opt the component out with `[Component(Compiler = false)]`.
  A scope reading something no dependency stands for (a `MutableRef`, a `static` field that is not `readonly`, a
  method of your own, a clock or random source named in `Documentation~/memoization.md`, an instance property of
  a `System.Diagnostics` or `System.Threading` type such as `Stopwatch.Elapsed`) is built on every render the
  component's own cache misses, as it was. The scopes cached and the ones not are listed there.
- A component whose body a hook or a call used to keep out of the automatic memoization is now woven.
  A call ahead of the last hook that may run a hook the weaver cannot see (an interface or virtual dispatch, or a
  method of an assembly the post-processor cannot read) no longer leaves the component unwoven: the gate declines
  to hit on a render in which a hook ran inside it. `Use` and `UseFallback` are on the allow-lists, and a component
  calling `UseMutation`, `UseBlocker`, or a hook the weaver has not classified is woven with a gate that never hits,
  so that the scopes not reading what the hook returns are still cached. A component that takes a `UseMutation`
  result and keeps its UI in step with it needs no change.
  A scope no longer caches what it builds from a list or object that a call outside every scope is given, such as
  a `Grow(items)` statement in the body: it is built on every render the gate misses, as before.

### Added

- `VELVET_REPORT_AUTOMEMO`, an assembly scripting define symbol that makes the post-processor emit one warning per
  `[Component]` saying why it is left unwoven or what was woven into it.
- `Hooks.BeginOpaqueCall` and `Hooks.EndOpaqueCall`, the bracket the compiler transform weaves around a call it
  cannot prove runs no hook. They are not for calling by hand.
- `Hooks.TryGetMemoizedScope` and `Hooks.StoreMemoizedScope` for a cached `VNode`, and `Hooks.TryGetMemoizedValue`
  and `Hooks.StoreMemoizedValue` for any other cached value, the pairs the compiler transform weaves. They are not
  for calling by hand.
