### Changed

- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` keep an observer of its own for each query of a list that reads
  the same entry as another, the observers held over an entry going to its queries in the list's order, as TanStack
  Query v5 does since 5.90.11. Once the list committed again, such queries used to share the last observer held over
  the entry, so `Query.GetObserversCount()` counted one for all of them, and that commit re-rendered the component.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` with a `combine` hand it, at a change to one query, each other
  query's result as the hook last committed or notified it, as TanStack Query v5's `#onUpdate` does, where they used
  to compute every result afresh. A change a query's `NotifyOnChangeProps` leaves out reaches `combine` once a commit
  follows: two queries summed, the first listing only `Error`, whose data moves from 1 to 2 and then the second's
  from 10 to 20, render 21 and then 22, where they rendered 22 alone.
- A custom property in a USS file holding an `rgb()` of three numbers or an `rgba()` of at least four, whose arguments
  are anything but three integers, all separated by commas or all by spaces, and in `rgba()` a number after them,
  after a further comma where commas separate them and after a `/` where spaces do, is stored as the text it was
  written with, where UI Toolkit's importer stored the colour of the first three or four of those numbers alone: one
  with a percentage (`rgb(0 0 0 / 50%)`, stored as opaque black), a `var()`
  (`rgb(59 130 246 / var(--tw-ring-opacity))`), a fraction in a channel (`rgb(0.5, 0, 0)`, stored unscaled), a word or
  a hex colour among its numbers (`rgb(1, 2, 3, foo)`), a full stop no digit follows (`rgb(5., 0, 0)`), separators
  mixed, missing or out of place (`rgb(1, 2 3)`, `rgba(0 0 0 0.5)`, `rgba(255 0 / 0 0.5)`), or more numbers than its
  channels (`rgba(255, 0, 0, 1, 9)`). A stock USS `color: var(--x)` reads no colour from such a property.
- A custom property in a USS file that UI Toolkit's importer used to store as other tokens than CSS reads is stored
  as the text it was written with, as one holding a `rem` is, such as an integer a full stop follows with
  no digit after it (`3.px`, stored as `3 px`, and `3.`, stored as `3`), a unit a `-` or `_` continues (`1px_a`,
  stored as `1px _a`, and `1px-2px`, stored as `1px -2px`), a hex colour a name character follows (`#abcxyz`, stored
  as `#aabbccff xyz`), a colon (`a:b`, stored as `a b`), and a comment opening the arguments of `none()` or
  `filter()` (`filter(/* c */ a)`, stored with the comment's text as its first argument).
