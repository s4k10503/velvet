### Added

- VEL012 reports a field or property of `this` that the `_Impl` of an instance `[MemoizeMethod]` method reads
  and the memo does not key on: any but a `static`, `const` or `readonly` field and a `static` or init-only
  property, on a class or an interface. The cached node was served after such a member changed, with nothing
  reported. Pass the value to the method as a parameter.

### Fixed

- An instance `[MemoizeMethod]` method keys its memo on the instance it is called on, as well as on its
  arguments. Called at one position on another instance with equal arguments, it served the node the first
  instance built.

- `[MemoizeMethod]` escapes a contextual keyword — `record`, `required`, `scoped`, `file` and the rest — used as
  the name of a type, a type parameter, a namespace, a parameter or the method in its generated wrapper. A type
  named `@record` drew CS8860 from the generated code, and one named `@required`, `@scoped` or `@file` failed to
  compile there from C# 11 on.
