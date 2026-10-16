### Changed

- A scope now covers a child wherever it stands (an argument, a member of a props record, an arm of a conditional,
  the returned root), not only an element of a children list. A scope that ends where an enclosing scope ends (the
  last arm of a conditional that is itself a scope) is left to that one. A scope may also call a plain property
  read (a member of a type of your own assembly, or an auto-property) and the members of `System`,
  `System.Collections`, `System.Collections.Generic`, `System.Linq`, `System.Text` and
  `System.Runtime.CompilerServices`, and read a `readonly` static of a value type or `string`, and a local of a
  value type or `string`. `Random`, `Environment`, `WeakReference` and `Lazy<T>` and the
  static members of `DateTime`, `DateTimeOffset`, `Guid` and `TimeZoneInfo` are left out, and so is any other
  property read (`Rigidbody.velocity`, `Task.IsCompleted`, `Stopwatch.Elapsed`, `Store<T>.Current`): a scope calling
  one is built on every render the component's own cache misses, as it was. A property read or a `System` member
  that a scope's expression calls no longer runs on a render where none of the values the scope reads changed. A
  body whose weave does not keep the stack in order is woven with its gate alone.
