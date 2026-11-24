### Fixed

- A `has-[.class]:` or `has-[:checked]:` element above a `V.VirtualList` kept what it matched before a range the list
  rendered from a scroll or a resize: it did not match a row carrying the class, or a toggle checked, that the range
  brought in, and it kept matching a row the range took away, until a later render re-derived it. It is now
  re-derived once the range has rendered, before the rows' own layout effects run, as `:has()` matches whatever
  changed the subtree. A `has-[.class]:` element above the target a row's `V.Portal` mounts its children in now
  matches a child carrying the class that such a range mounts there, which it did not.
