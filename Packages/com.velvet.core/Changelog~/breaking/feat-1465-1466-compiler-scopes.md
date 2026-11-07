### Changed

- A `[Component]` whose hook value is never read, such as `var (_, bump) = Hooks.UseState(0)` kept to force a
  render, is no longer left wholly unwoven. It has no gate on the body as a whole, since the render the hook causes
  changes nothing such a gate is keyed on, and its parts are cached on their own like any other body's. A render the
  setter causes now builds again only the parts that read something the build cannot track — a mutable static, a
  call whose target it cannot see, a stable object a call may have changed — and hands the reconciler every other
  part by instance, so a subtree that reads only parameters and hook values is not built again by it, as in
  React Compiler, where each reactive scope is recomputed according to the values it
  reads and not according to whether the component rendered.
- The code that computes what a local is given, such as `var visible = items.Where(Matches).ToArray();`, is cached
  in a part of its own when a cached part reads that local as a whole and nothing writes into the local afterwards.
  It keeps its identity while the values it reads are unchanged, so the parts that read it are no longer built
  again on every render in which something else changed, where the array was rebuilt each time and every part
  keyed on it missed. A body that expected a fresh array, list or delegate on each render, by comparing it with an
  earlier one, sees the same instance until its inputs change; a local the body writes into, or hands to any call
  other than a pure one, is still computed on every render. The memoization
  guide states which computations are cached.

### Added

- `Hooks.TryGetValueScope` and `Hooks.StoreValueScope`, the runtime half of the above for a part that caches a
  value rather than a `VNode`, which the build-time transform calls and nothing else should.
