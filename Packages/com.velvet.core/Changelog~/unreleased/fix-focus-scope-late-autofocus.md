### Fixed

- A `V.FocusScope` with `autoFocus` that mounted after its parent, in the tree or inside a `V.Portal` that already held
  content, never took focus. It now takes focus when it mounts.
