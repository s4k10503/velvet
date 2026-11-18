### Fixed

- A `V.FocusScope` with `autoFocus` inside a `V.Portal` that mounted while its `V.Suspense` kept its children hidden
  focused its button while `display: none` hid it, and a scope hidden at its mount took no focus later. Its `autoFocus`
  now waits for the boundary to reveal the scope, as React defers an Offscreen subtree's host autofocus to the reveal.
