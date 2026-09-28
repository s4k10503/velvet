### Fixed

- `[MemoizeMethod]` generates its wrapper for a method of any number of parameters, and for a generic method,
  repeating its type parameters and their constraints. Nine or more parameters were reported as VEL002 and a
  generic method as VEL003, and neither got an implementation, so the declaration failed to compile. Both
  diagnostics are gone.

- `[MemoizeMethod]` accepts an `in` parameter, memoizing on a copy of it; VEL005 now reports only `ref` and
  `out`. A `params` parameter, an extension method's `this`, and the `new`, `virtual`, `override`, `sealed`
  and `readonly` modifiers are repeated on the generated implementation, and a parameter named after a C#
  keyword is escaped there; each of these made it fail to compile. So did an instance method of a struct, whose
  wrapper now calls `_Impl` on a copy of `this`, which a lambda in a struct cannot capture.

- A `[MemoizeMethod]` method taking no parameters no longer warns (VEL001) when the generator cannot prove its
  `_Impl` pure. Its empty dependency list is a complete declaration, as `useMemo(factory, [])` is in React, and
  VEL001 is gone.

- VEL008 reports a `[MemoizeMethod]` return type deriving from `VNode` other than `MemoNode`. It accepted any
  such type, and the generated body, which returns a `MemoNode`, then failed to compile.

- VEL010 reports a `[MemoizeMethod]` parameter of a ref struct type such as `Span<T>`, or of a pointer type.
  Neither can be a dependency, and the generated wrapper failed to compile with nothing naming the parameter.

- A `[MemoizeMethod]` method returning `VelvetTask<VNode>` is reported as VEL004, as one returning
  `Task<VNode>` is, rather than as VEL008.
