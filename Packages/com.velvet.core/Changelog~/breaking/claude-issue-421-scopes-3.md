### Changed

- A scope now covers a child wherever it stands (an argument, a member of a props record, an arm of a conditional,
  the returned root), not only an element of a children list, and it may call a property accessor and the members
  of `System`, `System.Collections`, `System.Collections.Generic`, `System.Linq`, `System.Text` and
  `System.Runtime.CompilerServices`, and read a `readonly` static of a value type or `string`, and a local of a
  value type or `string`. `Random`, `Environment` and the static members of `DateTime`, `DateTimeOffset`, `Guid`
  and `TimeZoneInfo` are left out, and so are the instance property accessors of the `System.Diagnostics` and
  `System.Threading` types (`Stopwatch.Elapsed`): a scope calling one is built on every render the component's
  own cache misses, as it was. A property accessor or a `System` member that a scope's expression calls no longer
  runs on a render where none of the values the scope reads changed. A body whose weave does not keep the stack
  in order is woven with its gate alone.
