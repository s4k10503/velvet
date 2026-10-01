### Fixed

- A stylesheet whose `@import` list is edited in place during an editor session is read again the next time
  the missing-stylesheet warning or a portal host looks at it, rather than answered from what it imported
  before the edit.
