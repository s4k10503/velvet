### Added

- `V.Fragment(a, b, …)` takes its children as `params` arguments, the counterpart of `<>{a}{b}</>`, so a
  Fragment needs no `new VNode?[] { … }` around them.

### Fixed

- A `V.VirtualList` item whose key repeats one another item returns renders, under a warning naming the
  key when both render in one range. Items sharing a key are told apart by their item index: each keeps the
  row rendered at its own index while it stays in the range, including across a range change that scrolls
  the other out of the range or back into it. The repeated item was left out of the range.
