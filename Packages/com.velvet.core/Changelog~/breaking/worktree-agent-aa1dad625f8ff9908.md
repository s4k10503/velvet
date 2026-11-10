### Changed

- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` keep an observer of its own for each query of a list that reads
  the same entry as another, the observers held over an entry going to its queries in the list's order, as TanStack
  Query v5 does since 5.90.11. Once the list committed again, such queries used to share the last observer held over
  the entry, so `Query.GetObserversCount()` counted one for all of them, and that commit re-rendered the component.
- A custom property in a USS file whose number UI Toolkit's importer used to store as other tokens than CSS reads is
  stored as the text it was written with, as one holding a `rem` is, such as an integer a full stop follows with
  no digit after it (`3.px`, stored as `3 px`, and `3.`, stored as `3`) and a unit a `-` or `_` continues (`1px_a`,
  stored as `1px _a`, and `1px-2px`, stored as `1px -2px`).
