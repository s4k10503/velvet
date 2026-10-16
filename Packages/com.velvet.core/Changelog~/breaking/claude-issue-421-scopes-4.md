### Changed

- A lambda that captures variables and a value the body derives and stores once into a local are now scopes
  too, on the values they read. A lambda is cached under the captured variables it reads when it runs, and a
  derived value (`var visible = items.Where(...)`) is cached when nothing can change it afterwards: a value
  type, a `string`, a delegate, or an `IEnumerable<T>` / `IReadOnlyList<T>`-style interface. A `List<T>` or an
  array is not one, because the body may write to it later. A value that can hold a node (a tuple, a struct or a lookup of nodes) is not one either. The delegate a render hands the reconciler is
  therefore the same instance on a render where nothing it captures changed.

### Added

- `Hooks.TryGetMemoizedValue` and `Hooks.StoreMemoizedValue`, the pair the compiler transform weaves around a
  cached value that is not a `VNode`. They are not for calling by hand.
