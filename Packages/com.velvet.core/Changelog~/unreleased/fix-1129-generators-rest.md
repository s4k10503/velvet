### Added

- VEL012 reports a field or property of `this` that the `_Impl` of an instance `[MemoizeMethod]` method reads
  while it runs and the memo does not key on: any but a `static`, `const` or `readonly` field and a `static`,
  init-only or get-only auto-property, on a class or an interface. A read inside a lambda or local function
  taken to run after `_Impl` returns, such as an event handler, is not reported. The cached node was served
  after such a member changed, with nothing reported. Pass the value to the method as a parameter.

### Fixed

- An instance `[MemoizeMethod]` method whose `_Impl` is an instance method keys its memo on the instance it is
  called on, as well as on its arguments. Called at one position on another instance with equal arguments, it
  served the node the first instance built.

- `[MemoizeMethod]` escapes a contextual keyword Roslyn classifies as one — `record`, `required`, `scoped` and
  `file` among them — used as the name of a type, a type parameter, a namespace, a parameter or the method in
  its generated wrapper. A type named `@record` drew CS8860 from the generated code, and one named
  `@required`, `@scoped` or `@file` failed to compile there from C# 11 on.

- VEL100 no longer reports a get-only auto-property a hook lambda captures. It is assigned only at
  construction, as a `readonly` field is, which VEL100 already left out.
