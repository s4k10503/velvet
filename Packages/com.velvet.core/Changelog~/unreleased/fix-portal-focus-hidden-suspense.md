### Fixed

- A `V.FocusScope` with `autoFocus` inside a `V.Portal` that mounted while its `V.Suspense` kept its children hidden
  could take focus while `display: none` hid its button. Its `autoFocus` now waits for the boundary to reveal the
  scope and takes focus once the revealed content is styled, unless focus has moved elsewhere or the boundary hides the
  scope again by then. React runs the `autoFocus` of a host that appears with a reveal.
