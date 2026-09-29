### Fixed

- `[MemoizeMethod]` generates its wrapper for a method of any number of parameters, and for a generic method,
  repeating its type parameters and their constraints. Nine or more parameters were reported as VEL002 and a
  generic method as VEL003, and neither got an implementation, so the declaration failed to compile. Both
  diagnostics are gone. A generic method's type arguments are part of the memo's dependencies, so a call at
  the same position with another type argument rebuilds.

- `[MemoizeMethod]` accepts an `in` parameter, memoizing on a copy of it, including an extension method's
  `this in` receiver; VEL005 now reports only `ref` and `out`. A `params` parameter, an extension method's
  `this`, and the `new`, `virtual`, `override`, `sealed`, `readonly` and `unsafe` modifiers are repeated on the
  generated implementation, as are the `class` and `default` constraints a generic override states; a C#
  keyword used as the name of a parameter, a type parameter, a type or a namespace is escaped there; and a
  method declared in an interface is implemented in a partial interface. Each of these left the declaration
  without an implementation that compiles.

- A `[MemoizeMethod]` `params` array is compared element by element, behind its length, so a call with equal
  arguments reuses the cached node. The array the compiler builds afresh at every call was the dependency
  itself and never compared equal once the method had another parameter or the array's elements were a value
  type. Any other array parameter is one dependency compared by instance; where it was the only parameter and
  its elements were a reference type, it became the whole dependency list, compared element by element, or,
  passed null, no list at all, which rebuilt every render.

- A `[MemoizeMethod]` method taking no parameters no longer warns (VEL001) when the generator cannot prove its
  `_Impl` pure. Its empty dependency list is a complete declaration, as `useMemo(factory, [])` is in React, and
  VEL001 is gone.

- VEL008 reports a `[MemoizeMethod]` return type deriving from `VNode` other than `MemoNode`, and a return by
  reference. It accepted both, and the generated body, which returns a `MemoNode` by value, then failed to
  compile.

- VEL010 reports a `[MemoizeMethod]` parameter of a ref struct type such as `Span<T>`, of a pointer type, or of
  an array of pointers. None of them can be a dependency, and the generated wrapper failed to compile instead.

- VEL011 reports a `[MemoizeMethod]` instance member of a struct that is not `readonly`, or of a `ref struct`.
  The factory calls `_Impl` on a copy of `this`, which a lambda in a struct cannot capture, so a write to the
  struct would be lost; a ref struct cannot be copied anywhere the factory can reach. A `readonly` member of
  any other struct is supported.

- A `[MemoizeMethod]` method returning `VelvetTask<VNode>` is reported as VEL004, as one returning
  `Task<VNode>` is, rather than as VEL008.
