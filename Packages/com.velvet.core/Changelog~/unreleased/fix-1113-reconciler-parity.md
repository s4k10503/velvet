### Added

- `V.Fragment(a, b, …)` takes its children as `params` arguments, the counterpart of `<>{a}{b}</>`, so a
  Fragment needs no `new VNode?[] { … }` around them.

### Fixed

- A `V.VirtualList` item whose `keySelector` returns a key holding a NUL (U+0000) renders. It was left out
  of the rendered range under a warning, although that key never becomes a `VNode.Key` or a scope segment
  and is compared only with the list's other keys.

- A `V.VirtualList` item whose key repeats one an earlier item of the rendered range returned renders, still
  under a warning naming the key, and is matched across range changes by its item index as an item with a
  `null` key is. It was left out of the range.
