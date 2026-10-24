### Changed

- Automatic memoization of a `[Component]` body now caches the parts of the body on their own as well as the
  body as a whole. A call that builds a `VNode`, with the code that builds its arguments, is keyed on only the
  values it reads, so a render in which one state changed builds again only the labels and subtrees that read it
  and hands the reconciler the rest by instance. A props record read through its fields or automatic properties
  is keyed member by member. A part that takes the result of a call the build cannot see, of a hook outside the
  weaver's allow-lists, or of a stable object a call may have changed is built on every render and its siblings
  are not. The memoization guide states what keeps a part from being cached.
- A call the build cannot see (an interface or virtual call, or one into an assembly it cannot load) ahead of the
  last hook, a hook outside the allow-lists, and a hook result used in a shape the weaver cannot key on no longer
  leave the whole component without memoization: the body as a whole is not keyed, and the parts that read none
  of them still are. A whole tuple a hook returned and the body reads one element at a time is keyed on those
  elements.

### Added

- `MemoizationReportAttribute`, written onto each `[Component]` method by the build-time transform: whether the
  body as a whole is keyed, how many parts are cached on their own, and one line per thing left uncached with the
  reason. An assembly carrying `AssemblyMetadata` with `MemoizationReportAttribute.MetadataKey` and
  `MemoizationReportAttribute.WarnValue` has the same lines reported as build warnings.
- `Hooks.ReserveScopes`, `Hooks.TryGetScope` and `Hooks.StoreScope`, the runtime half of the above, which the
  build-time transform calls and nothing else should.
