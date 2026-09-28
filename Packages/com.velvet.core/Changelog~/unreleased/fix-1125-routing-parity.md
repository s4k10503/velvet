### Fixed

- A `Router`'s history keeps every entry, as a memory router's does. It used to drop its oldest entry
  once it held 50, so walking back from a long session stopped short of where it began.
